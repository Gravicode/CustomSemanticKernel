﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.AI.ImageGeneration;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Security;
using Microsoft.SemanticKernel.SemanticFunctions;
using Microsoft.SemanticKernel.Services;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.TemplateEngine;

namespace Microsoft.SemanticKernel;

/// <summary>
/// Semantic kernel class.
/// The kernel provides a skill collection to define native and semantic functions, an orchestrator to execute a list of functions.
/// Semantic functions are automatically rendered and executed using an internal prompt template rendering engine.
/// Future versions will allow to:
/// * customize the rendering engine
/// * include branching logic in the functions pipeline
/// * persist execution state for long running pipelines
/// * distribute pipelines over a network
/// * RPC functions and secure environments, e.g. sandboxing and credentials management
/// * auto-generate pipelines given a higher level goal
/// </summary>
public sealed class Kernel : IKernel, IDisposable
{
    /// <inheritdoc/>
    public KernelConfig Config { get; }

    /// <inheritdoc/>
    public ILogger Log { get; }

    /// <inheritdoc/>
    public ISemanticTextMemory Memory => this._memory;

    /// <inheritdoc/>
    public IReadOnlySkillCollection Skills => this._skillCollection.ReadOnlySkillCollection;

    /// <inheritdoc/>
    public IPromptTemplateEngine PromptTemplateEngine { get; }

    /// <inheritdoc/>
    public ITrustService? TrustServiceInstance => this._trustService;

    /// <summary>
    /// Return a new instance of the kernel builder, used to build and configure kernel instances.
    /// </summary>
    public static KernelBuilder Builder => new();

    /// <summary>
    /// Kernel constructor. See KernelBuilder for an easier and less error prone approach to create kernel instances.
    /// </summary>
    /// <param name="skillCollection"></param>
    /// <param name="aiServiceProvider"></param>
    /// <param name="promptTemplateEngine"></param>
    /// <param name="memory"></param>
    /// <param name="config"></param>
    /// <param name="log"></param>
    /// <param name="trustService"></param>
    public Kernel(
        ISkillCollection skillCollection,
        IAIServiceProvider aiServiceProvider,
        IPromptTemplateEngine promptTemplateEngine,
        ISemanticTextMemory memory,
        KernelConfig config,
        ILogger log,
        ITrustService? trustService = null)
    {
        this.Log = log;
        this.Config = config;
        this.PromptTemplateEngine = promptTemplateEngine;
        this._memory = memory;
        this._aiServiceProvider = aiServiceProvider;
        this._promptTemplateEngine = promptTemplateEngine;
        this._skillCollection = skillCollection;
        this._trustService = trustService;
    }

    /// <inheritdoc/>
    public ISKFunction RegisterSemanticFunction(string functionName, SemanticFunctionConfig functionConfig, ITrustService? trustService = null)
    {
        return this.RegisterSemanticFunction(SkillCollection.GlobalSkill, functionName, functionConfig, trustService);
    }

    /// <inheritdoc/>
    public ISKFunction RegisterSemanticFunction(string skillName, string functionName, SemanticFunctionConfig functionConfig, ITrustService? trustService = null)
    {
        // Future-proofing the name not to contain special chars
        Verify.ValidSkillName(skillName);
        Verify.ValidFunctionName(functionName);

        ISKFunction function = this.CreateSemanticFunction(skillName, functionName, functionConfig, trustService);
        this._skillCollection.AddFunction(function);

        return function;
    }

    /// <inheritdoc/>
    public IDictionary<string, ISKFunction> ImportSkill(object skillInstance, string? skillName = null, ITrustService? trustService = null)
    {
        Verify.NotNull(skillInstance);

        if (string.IsNullOrWhiteSpace(skillName))
        {
            skillName = SkillCollection.GlobalSkill;
            this.Log.LogTrace("Importing skill {0} in the global namespace", skillInstance.GetType().FullName);
        }
        else
        {
            this.Log.LogTrace("Importing skill {0}", skillName);
        }

        Dictionary<string, ISKFunction> skill = ImportSkill(
            skillInstance,
            skillName!,
            // Use the default trust service registered if none is provided
            trustService ?? this.TrustServiceInstance,
            this.Log
        );
        foreach (KeyValuePair<string, ISKFunction> f in skill)
        {
            f.Value.SetDefaultSkillCollection(this.Skills);
            this._skillCollection.AddFunction(f.Value);
        }

        return skill;
    }

    /// <inheritdoc/>
    public ISKFunction RegisterCustomFunction(ISKFunction customFunction)
    {
        // Note this does not accept the trustService, it is already defined
        // when the custom function is created, so the kernel will not override

        Verify.NotNull(customFunction);

        customFunction.SetDefaultSkillCollection(this.Skills);
        this._skillCollection.AddFunction(customFunction);

        return customFunction;
    }

    /// <inheritdoc/>
    public void RegisterMemory(ISemanticTextMemory memory)
    {
        this._memory = memory;
    }

    /// <inheritdoc/>
    public Task<SKContext> RunAsync(params ISKFunction[] pipeline)
        => this.RunAsync(new ContextVariables(), pipeline);

    /// <inheritdoc/>
    public Task<SKContext> RunAsync(string input, params ISKFunction[] pipeline)
        => this.RunAsync(new ContextVariables(input), pipeline);

    /// <inheritdoc/>
    public Task<SKContext> RunAsync(ContextVariables variables, params ISKFunction[] pipeline)
        => this.RunAsync(variables, CancellationToken.None, pipeline);

    /// <inheritdoc/>
    public Task<SKContext> RunAsync(CancellationToken cancellationToken, params ISKFunction[] pipeline)
        => this.RunAsync(new ContextVariables(), cancellationToken, pipeline);

    /// <inheritdoc/>
    public Task<SKContext> RunAsync(string input, CancellationToken cancellationToken, params ISKFunction[] pipeline)
        => this.RunAsync(new ContextVariables(input), cancellationToken, pipeline);

    /// <inheritdoc/>
    public async Task<SKContext> RunAsync(ContextVariables variables, CancellationToken cancellationToken, params ISKFunction[] pipeline)
    {
        var context = new SKContext(
            variables,
            this._memory,
            this._skillCollection.ReadOnlySkillCollection,
            this.Log,
            cancellationToken);

        int pipelineStepCount = -1;
        foreach (ISKFunction f in pipeline)
        {
            if (context.ErrorOccurred)
            {
                this.Log.LogError(
                    context.LastException,
                    "Something went wrong in pipeline step {0}:'{1}'", pipelineStepCount, context.LastErrorDescription);
                return context;
            }

            pipelineStepCount++;

            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context = await f.InvokeAsync(context).ConfigureAwait(false);

                if (context.ErrorOccurred)
                {
                    this.Log.LogError("Function call fail during pipeline step {0}: {1}.{2}. Error: {3}",
                        pipelineStepCount, f.SkillName, f.Name, context.LastErrorDescription);
                    return context;
                }
            }
            catch (Exception e) when (!e.IsCriticalException())
            {
                this.Log.LogError(e, "Something went wrong in pipeline step {0}: {1}.{2}. Error: {3}",
                    pipelineStepCount, f.SkillName, f.Name, e.Message);
                context.Fail(e.Message, e);
                return context;
            }
        }

        return context;
    }

    /// <inheritdoc/>
    public ISKFunction Func(string skillName, string functionName)
    {
        return this.Skills.GetFunction(skillName, functionName);
    }

    /// <inheritdoc/>
    public SKContext CreateNewContext(CancellationToken cancellationToken = default)
    {
        return new SKContext(
            memory: this._memory,
            skills: this._skillCollection.ReadOnlySkillCollection,
            logger: this.Log,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public T GetService<T>(string? name = null) where T : IAIService
    {
        var service = this._aiServiceProvider.GetService<T>(name);
        if (service != null)
        {
            return service;
        }

        if (typeof(T) == typeof(ITextCompletion))
        {
            name ??= this.Config.DefaultServiceId;

#pragma warning disable CS0618 // Type or member is obsolete
            if (!this.Config.TextCompletionServices.TryGetValue(name, out Func<IKernel, ITextCompletion> factory))
            {
                throw new KernelException(KernelException.ErrorCodes.ServiceNotFound, $"'{name}' text completion service not available");
            }

            var serv = factory.Invoke(this);
            return (T)serv;
        }

        if (typeof(T) == typeof(IEmbeddingGeneration<string, float>))
        {
            name ??= this.Config.DefaultServiceId;

            if (!this.Config.TextEmbeddingGenerationServices.TryGetValue(name, out Func<IKernel, IEmbeddingGeneration<string, float>> factory))
            {
                throw new KernelException(KernelException.ErrorCodes.ServiceNotFound, $"'{name}' text embedding service not available");
            }

            var serv = factory.Invoke(this);
            return (T)serv;
        }

        if (typeof(T) == typeof(IChatCompletion))
        {
            name ??= this.Config.DefaultServiceId;

            if (!this.Config.ChatCompletionServices.TryGetValue(name, out Func<IKernel, IChatCompletion> factory))
            {
                throw new KernelException(KernelException.ErrorCodes.ServiceNotFound, $"'{name}' chat completion service not available");
            }

            var serv = factory.Invoke(this);
            return (T)serv;
        }

        if (typeof(T) == typeof(IImageGeneration))
        {
            name ??= this.Config.DefaultServiceId;

            if (!this.Config.ImageGenerationServices.TryGetValue(name, out Func<IKernel, IImageGeneration> factory))
            {
                throw new KernelException(KernelException.ErrorCodes.ServiceNotFound, $"'{name}' image generation service not available");
            }

            var serv = factory.Invoke(this);
            return (T)serv;
        }
#pragma warning restore CS0618 // Type or member is obsolete

        throw new KernelException(KernelException.ErrorCodes.ServiceNotFound, $"Service of type {typeof(T)} and name {name ?? "<NONE>"} not registered.");
    }

    /// <summary>
    /// Dispose of resources.
    /// </summary>
    public void Dispose()
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (this._memory is IDisposable mem) { mem.Dispose(); }

        // ReSharper disable once SuspiciousTypeConversion.Global
        if (this._skillCollection is IDisposable reg) { reg.Dispose(); }
    }

    #region private ================================================================================

    private readonly ISkillCollection _skillCollection;
    private ISemanticTextMemory _memory;
    private readonly IPromptTemplateEngine _promptTemplateEngine;
    private readonly IAIServiceProvider _aiServiceProvider;
    private ITrustService? _trustService;

    private ISKFunction CreateSemanticFunction(
        string skillName,
        string functionName,
        SemanticFunctionConfig functionConfig,
        ITrustService? trustService = null)
    {
        if (!functionConfig.PromptTemplateConfig.Type.Equals("completion", StringComparison.OrdinalIgnoreCase))
        {
            throw new AIException(
                AIException.ErrorCodes.FunctionTypeNotSupported,
                $"Function type not supported: {functionConfig.PromptTemplateConfig}");
        }

        ISKFunction func = SKFunction.FromSemanticConfig(
            skillName,
            functionName,
            functionConfig,
            // Use the default trust service registered if none is provided
            trustService ?? this.TrustServiceInstance,
            this.Log
        );

        // Connect the function to the current kernel skill collection, in case the function
        // is invoked manually without a context and without a way to find other functions.
        func.SetDefaultSkillCollection(this.Skills);

        func.SetAIConfiguration(CompleteRequestSettings.FromCompletionConfig(functionConfig.PromptTemplateConfig.Completion));

        // Note: the service is instantiated using the kernel configuration state when the function is invoked
        func.SetAIService(() => this.GetService<ITextCompletion>());

        return func;
    }

    /// <summary>
    /// Import a skill into the kernel skill collection, so that semantic functions and pipelines can consume its functions.
    /// </summary>
    /// <param name="skillInstance">Skill class instance</param>
    /// <param name="skillName">Skill name, used to group functions under a shared namespace</param>
    /// <param name="trustService">Service used for trust checks</param>
    /// <param name="log">Application logger</param>
    /// <returns>Dictionary of functions imported from the given class instance, case-insensitively indexed by name.</returns>
    private static Dictionary<string, ISKFunction> ImportSkill(object skillInstance, string skillName, ITrustService? trustService, ILogger log)
    {
        MethodInfo[] methods = skillInstance.GetType().GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public);
        log.LogTrace("Importing skill name: {0}. Potential methods found: {1}", skillName, methods.Length);

        // Filter out non-SKFunctions and fail if two functions have the same name
        Dictionary<string, ISKFunction> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (MethodInfo method in methods)
        {
            if (method.GetCustomAttribute<SKFunctionAttribute>() is not null)
            {
                ISKFunction function = SKFunction.FromNativeMethod(method, skillInstance, skillName, trustService, log);
                if (result.ContainsKey(function.Name))
                {
                    throw new KernelException(
                        KernelException.ErrorCodes.FunctionOverloadNotSupported,
                        "Function overloads are not supported, please differentiate function names");
                }

                result.Add(function.Name, function);
            }
        }

        log.LogTrace("Methods imported {0}", result.Count);

        return result;
    }

    #endregion
}
