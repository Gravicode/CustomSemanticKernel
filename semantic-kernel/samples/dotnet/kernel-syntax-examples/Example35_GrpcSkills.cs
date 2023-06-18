﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Skills.Grpc.Extensions;
using RepoUtils;

/**
 * This example shows how to use gRPC skills.
 */

// ReSharper disable once InconsistentNaming
public static class Example35_GrpcSkills
{
    public static async Task RunAsync()
    {
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Log).Build();

        // Import a gRPC skill using one of the following Kernel extension methods
        // kernel.RegisterGrpcSkill
        // kernel.ImportGrpcSkillFromDirectory
        var skill = kernel.ImportGrpcSkillFromFile("<skill-name>", "<path-to-.proto-file>");

        // Add arguments for required parameters, arguments for optional ones can be skipped.
        var contextVariables = new ContextVariables();
        contextVariables.Set("address", "<gRPC-server-address>");
        contextVariables.Set("payload", "<gRPC-request-message-as-json>");

        // Run
        var result = await kernel.RunAsync(contextVariables, skill["<operation-name>"]);

        Console.WriteLine("Skill response: {0}", result);
    }
}
