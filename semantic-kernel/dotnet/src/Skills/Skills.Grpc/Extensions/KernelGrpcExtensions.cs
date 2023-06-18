﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.Grpc.Model;
using Microsoft.SemanticKernel.Skills.Grpc.Protobuf;

namespace Microsoft.SemanticKernel.Skills.Grpc.Extensions;

/// <summary>
/// <see cref="IKernel"/> extensions methods for gRPC functionality.
/// </summary>
public static class KernelGrpcExtensions
{
    /// <summary>
    /// Imports gRPC document from a directory.
    /// </summary>
    /// <param name="kernel">Semantic Kernel instance.</param>
    /// <param name="parentDirectory">Directory containing the skill directory.</param>
    /// <param name="skillDirectoryName">Name of the directory containing the selected skill.</param>
    /// <returns>A list of all the semantic functions representing the skill.</returns>
    public static IDictionary<string, ISKFunction> ImportGrpcSkillFromDirectory(this IKernel kernel, string parentDirectory, string skillDirectoryName)
    {
        const string ProtoFile = "grpc.proto";

        Verify.ValidSkillName(skillDirectoryName);

        var skillDir = Path.Combine(parentDirectory, skillDirectoryName);
        Verify.DirectoryExists(skillDir);

        var filePath = Path.Combine(skillDir, ProtoFile);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"No .proto document for the specified path - {filePath} is found.");
        }

        kernel.Log.LogTrace("Registering gRPC functions from {0} .proto document", filePath);

        using var stream = File.OpenRead(filePath);

        return kernel.RegisterGrpcSkill(stream, skillDirectoryName);
    }

    /// <summary>
    /// Imports gRPC document from a file.
    /// </summary>
    /// <param name="kernel">Semantic Kernel instance.</param>
    /// <param name="skillName">Name of the skill to register.</param>
    /// <param name="filePath">File path to .proto document.</param>
    /// <returns>A list of all the semantic functions representing the skill.</returns>
    public static IDictionary<string, ISKFunction> ImportGrpcSkillFromFile(
        this IKernel kernel,
        string skillName,
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"No .proto document for the specified path - {filePath} is found.");
        }

        kernel.Log.LogTrace("Registering gRPC functions from {0} .proto document", filePath);

        using var stream = File.OpenRead(filePath);

        return kernel.RegisterGrpcSkill(stream, skillName);
    }

    /// <summary>
    /// Registers an gRPC skill.
    /// </summary>
    /// <param name="kernel">Semantic Kernel instance.</param>
    /// <param name="documentStream">.proto document stream.</param>
    /// <param name="skillName">Skill name.</param>
    /// <returns>A list of all the semantic functions representing the skill.</returns>
    public static IDictionary<string, ISKFunction> RegisterGrpcSkill(
        this IKernel kernel,
        Stream documentStream,
        string skillName)
    {
        Verify.NotNull(kernel);
        Verify.ValidSkillName(skillName);

        // Parse
        var parser = new ProtoDocumentParser();

        var operations = parser.Parse(documentStream, skillName);

        var skill = new Dictionary<string, ISKFunction>();

        var runner = new GrpcOperationRunner(new HttpClient());

        foreach (var operation in operations)
        {
            try
            {
                kernel.Log.LogTrace("Registering gRPC function {0}.{1}", skillName, operation.Name);
                var function = kernel.RegisterGrpcFunction(runner, skillName, operation);
                skill[function.Name] = function;
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                //Logging the exception and keep registering other gRPC functions
                kernel.Log.LogWarning(ex, "Something went wrong while rendering the gRPC function. Function: {0}.{1}. Error: {2}",
                    skillName, operation.Name, ex.Message);
            }
        }

        return skill;
    }

    #region private

    /// <summary>
    /// Registers SKFunction for a gRPC operation.
    /// </summary>
    /// <param name="kernel">Semantic Kernel instance.</param>
    /// <param name="runner">gRPC operation runner.</param>
    /// <param name="skillName">Skill name.</param>
    /// <param name="operation">The gRPC operation.</param>
    /// <returns>An instance of <see cref="SKFunction"/> class.</returns>
    private static ISKFunction RegisterGrpcFunction(
        this IKernel kernel,
        GrpcOperationRunner runner,
        string skillName,
        GrpcOperation operation)
    {
        var operationParameters = operation.GetParameters();

        async Task<SKContext> ExecuteAsync(SKContext context)
        {
            try
            {
                var arguments = new Dictionary<string, string>();

                //Extract function arguments from context
                foreach (var parameter in operationParameters)
                {
                    //A try to resolve argument parameter name.
                    if (context.Variables.TryGetValue(parameter.Name, out string? value))
                    {
                        arguments.Add(parameter.Name, value);
                        continue;
                    }

                    throw new KeyNotFoundException($"No variable found in context to use as an argument for the '{parameter.Name}' parameter of the '{skillName}.{operation.Name}' gRPC function.");
                }

                //SKFunction should be extended to pass cancellation token for delegateFunction calls.
                var result = await runner.RunAsync(operation, arguments, CancellationToken.None).ConfigureAwait(false);

                if (result != null)
                {
                    context.Variables.Update(result.ToString());
                }
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                kernel.Log.LogWarning(ex, "Something went wrong while rendering the gRPC function. Function: {0}.{1}. Error: {2}", skillName, operation.Name,
                    ex.Message);
                context.Fail(ex.Message, ex);
            }

            return context;
        }

        var function = SKFunction.FromNativeFunction(
            nativeFunction: ExecuteAsync,
            parameters: operationParameters.ToList(),
            description: operation.Name,
            skillName: skillName,
            functionName: operation.Name,
            isSensitive: false,
            log: kernel.Log);

        return kernel.RegisterCustomFunction(function);
    }

    #endregion
}
