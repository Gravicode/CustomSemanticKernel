﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Security;
using Microsoft.SemanticKernel.SemanticFunctions;

namespace Microsoft.SemanticKernel.SkillDefinition;

#pragma warning disable CS0618 // Temporarily suppressing Obsoletion warnings until obsolete attributes for compatibility are removed
#pragma warning disable format

/// <summary>
/// Standard Semantic Kernel callable function.
/// SKFunction is used to extend one C# <see cref="Delegate"/>, <see cref="Func{T, TResult}"/>, <see cref="Action"/>,
/// with additional methods required by the kernel.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SKFunction : ISKFunction, IDisposable
{
    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string SkillName { get; }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public bool IsSemantic { get; }

    /// <inheritdoc/>
    public bool IsSensitive { get; }

    /// <inheritdoc/>
    public ITrustService TrustServiceInstance => this._trustService;

    /// <inheritdoc/>
    public CompleteRequestSettings RequestSettings => this._aiRequestSettings;

    /// <summary>
    /// List of function parameters
    /// </summary>
    public IList<ParameterView> Parameters { get; }

    /// <summary>
    /// Create a native function instance, wrapping a native object method
    /// </summary>
    /// <param name="method">Signature of the method to invoke</param>
    /// <param name="target">Object containing the method to invoke</param>
    /// <param name="skillName">SK skill name</param>
    /// <param name="trustService">Service used for trust checks, if null the TrustService.DefaultTrusted implementation will be used</param>
    /// <param name="log">Application logger</param>
    /// <returns>SK function instance</returns>
    public static ISKFunction FromNativeMethod(
        MethodInfo method,
        object? target = null,
        string? skillName = null,
        ITrustService? trustService = null,
        ILogger? log = null)
    {
        if (!method.IsStatic && target is null)
        {
            throw new ArgumentNullException(nameof(target), "Argument cannot be null for non-static methods");
        }

        if (string.IsNullOrWhiteSpace(skillName))
        {
            skillName = SkillCollection.GlobalSkill;
        }

        MethodDetails methodDetails = GetMethodDetails(method, target, log);

        return new SKFunction(
            delegateFunction: methodDetails.Function,
            parameters: methodDetails.Parameters,
            skillName: skillName!,
            functionName: methodDetails.Name,
            isSemantic: false,
            description: methodDetails.Description,
            isSensitive: methodDetails.IsSensitive,
            trustService: trustService,
            log: log);
    }

    /// <summary>
    /// Create a native function instance, wrapping a delegate function
    /// </summary>
    /// <param name="nativeFunction">Function to invoke</param>
    /// <param name="skillName">SK skill name</param>
    /// <param name="functionName">SK function name</param>
    /// <param name="description">SK function description</param>
    /// <param name="parameters">SK function parameters</param>
    /// <param name="isSensitive">Whether the function is set to be sensitive (default false)</param>
    /// <param name="trustService">Service used for trust checks, if null the TrustService.DefaultTrusted implementation will be used</param>
    /// <param name="log">Application logger</param>
    /// <returns>SK function instance</returns>
    public static ISKFunction FromNativeFunction(
        Delegate nativeFunction,
        string? skillName = null,
        string? functionName = null,
        string? description = null,
        IEnumerable<ParameterView>? parameters = null,
        bool isSensitive = false,
        ITrustService? trustService = null,
        ILogger? log = null)
    {
        MethodDetails methodDetails = GetMethodDetails(nativeFunction.Method, nativeFunction.Target, log);

        functionName ??= nativeFunction.Method.Name;
        description ??= string.Empty;

        if (string.IsNullOrWhiteSpace(skillName))
        {
            skillName = SkillCollection.GlobalSkill;
        }

        return new SKFunction(
            delegateFunction: methodDetails.Function,
            parameters: parameters is not null ? parameters.ToList() : (IList<ParameterView>)Array.Empty<ParameterView>(),
            description: description,
            skillName: skillName!,
            functionName: functionName,
            isSemantic: false,
            // For native functions, do not read this from the methodDetails
            isSensitive: isSensitive,
            trustService: trustService,
            log: log);
    }

    /// <summary>
    /// Create a native function instance, given a semantic function configuration.
    /// </summary>
    /// <param name="skillName">Name of the skill to which the function to create belongs.</param>
    /// <param name="functionName">Name of the function to create.</param>
    /// <param name="functionConfig">Semantic function configuration.</param>
    /// <param name="trustService">Service used for trust checks, if null the TrustService.DefaultTrusted implementation will be used</param>
    /// <param name="log">Optional logger for the function.</param>
    /// <returns>SK function instance.</returns>
    public static ISKFunction FromSemanticConfig(
        string skillName,
        string functionName,
        SemanticFunctionConfig functionConfig,
        ITrustService? trustService = null,
        ILogger? log = null)
    {
        Verify.NotNull(functionConfig);

        Task<SKContext> LocalFuncTmp(
            ITextCompletion? client,
            CompleteRequestSettings? requestSettings,
            SKContext context)
        {
            return Task.FromResult(context);
        }

        var func = new SKFunction(
            // Start with an empty delegate, so we can have a reference to func
            // to be used in the LocalFunc below
            // Before returning the delegateFunction will be updated to be LocalFunc
            delegateFunction: LocalFuncTmp,
            parameters: functionConfig.PromptTemplate.GetParameters(),
            description: functionConfig.PromptTemplateConfig.Description,
            skillName: skillName,
            functionName: functionName,
            isSemantic: true,
            isSensitive: functionConfig.PromptTemplateConfig.IsSensitive,
            trustService: trustService,
            log: log
        );

        async Task<SKContext> LocalFunc(
            ITextCompletion? client,
            CompleteRequestSettings? requestSettings,
            SKContext context)
        {
            Verify.NotNull(client);
            Verify.NotNull(requestSettings);

            try
            {
                string renderedPrompt = await functionConfig.PromptTemplate.RenderAsync(context).ConfigureAwait(false);

                // Validates the rendered prompt before executing the completion
                // The prompt template might have function calls that could result in the context becoming untrusted,
                // this way this hook should check again if the context became untrusted
                TrustAwareString prompt = await func.TrustServiceInstance.ValidatePromptAsync(func, context, renderedPrompt).ConfigureAwait(false);
                var completionResults = await client.GetCompletionsAsync(prompt, requestSettings, context.CancellationToken).ConfigureAwait(false);
                string completion = await GetCompletionsResultContentAsync(completionResults, context.CancellationToken).ConfigureAwait(false);

                // Update the result with the completion
                context.Variables.UpdateKeepingTrustState(completion);

                // Flag the result as untrusted if the prompt has been considered untrusted
                if (!prompt.IsTrusted)
                {
                    context.UntrustResult();
                }
                context.ModelResults = completionResults.Select(c => c.ModelResult).ToArray();
            }
            catch (AIException ex)
            {
                const string Message = "Something went wrong while rendering the semantic function" +
                                       " or while executing the text completion. Function: {0}.{1}. Error: {2}. Details: {3}";
                log?.LogError(ex, Message, skillName, functionName, ex.Message, ex.Detail);
                context.Fail(ex.Message, ex);
            }
            catch (Exception ex) when (!ex.IsCriticalException())
            {
                const string Message = "Something went wrong while rendering the semantic function" +
                                       " or while executing the text completion. Function: {0}.{1}. Error: {2}";
                log?.LogError(ex, Message, skillName, functionName, ex.Message);
                context.Fail(ex.Message, ex);
            }

            return context;
        }

        // Update delegate function with a reference to the LocalFunc created
        func._function = LocalFunc;

        return func;
    }

    /// <inheritdoc/>
    public FunctionView Describe()
    {
        return new FunctionView
        {
            IsSemantic = this.IsSemantic,
            Name = this.Name,
            SkillName = this.SkillName,
            Description = this.Description,
            Parameters = this.Parameters,
        };
    }

    /// <inheritdoc/>
    public async Task<SKContext> InvokeAsync(SKContext context, CompleteRequestSettings? settings = null)
    {
        // If the function is invoked manually, the user might have left out the skill collection
        context.Skills ??= this._skillCollection;

        var validateContextResult = await this.TrustServiceInstance.ValidateContextAsync(this, context).ConfigureAwait(false);

        if (this.IsSemantic)
        {
            var resultContext = await this._function(this._aiService?.Value, settings ?? this._aiRequestSettings, context).ConfigureAwait(false);
            context.Variables.Update(resultContext.Variables);
        }
        else
        {
            try
            {
                context = await this._function(null, settings, context).ConfigureAwait(false);
            }
            catch (Exception e) when (!e.IsCriticalException())
            {
                const string Message = "Something went wrong while executing the native function. Function: {0}. Error: {1}";
                this._log.LogError(e, Message, this._function.Method.Name, e.Message);
                context.Fail(e.Message, e);
            }
        }

        // If the context has been considered untrusted, make sure the output of the function is also untrusted
        if (!validateContextResult)
        {
            context.UntrustResult();
        }

        return context;
    }

    /// <inheritdoc/>
    public Task<SKContext> InvokeAsync(
        string? input = null,
        CompleteRequestSettings? settings = null,
        ISemanticTextMemory? memory = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        SKContext context = new(
            new ContextVariables(input),
            memory: memory,
            skills: this._skillCollection,
            logger: logger,
            cancellationToken: cancellationToken);

        return this.InvokeAsync(context, settings);
    }

    /// <inheritdoc/>
    public ISKFunction SetDefaultSkillCollection(IReadOnlySkillCollection skills)
    {
        this._skillCollection = skills;
        return this;
    }

    /// <inheritdoc/>
    public ISKFunction SetAIService(Func<ITextCompletion> serviceFactory)
    {
        Verify.NotNull(serviceFactory);
        this.VerifyIsSemantic();
        this._aiService = new Lazy<ITextCompletion>(serviceFactory);
        return this;
    }

    /// <inheritdoc/>
    public ISKFunction SetAIConfiguration(CompleteRequestSettings settings)
    {
        Verify.NotNull(settings);
        this.VerifyIsSemantic();
        this._aiRequestSettings = settings;
        return this;
    }

    /// <summary>
    /// Dispose of resources.
    /// </summary>
    public void Dispose()
    {
        if (this._aiService is { IsValueCreated: true } aiService)
        {
            (aiService.Value as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// JSON serialized string representation of the function.
    /// </summary>
    public override string ToString()
        => this.ToString(false);

    /// <summary>
    /// JSON serialized string representation of the function.
    /// </summary>
    public string ToString(bool writeIndented)
        => JsonSerializer.Serialize(this, options: writeIndented ? s_toStringIndentedSerialization : s_toStringStandardSerialization);

    #region private

    private static readonly JsonSerializerOptions s_toStringStandardSerialization = new();
    private static readonly JsonSerializerOptions s_toStringIndentedSerialization = new() { WriteIndented = true };
    private Func<ITextCompletion?, CompleteRequestSettings?, SKContext, Task<SKContext>> _function;
    private readonly ILogger _log;
    private IReadOnlySkillCollection? _skillCollection;
    private Lazy<ITextCompletion>? _aiService = null;
    private CompleteRequestSettings _aiRequestSettings = new();
    private readonly ITrustService _trustService;

    private struct MethodDetails
    {
        public Func<ITextCompletion?, CompleteRequestSettings?, SKContext, Task<SKContext>> Function { get; set; }
        public List<ParameterView> Parameters { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsSensitive { get; set; }
    }

    private static async Task<string> GetCompletionsResultContentAsync(IReadOnlyList<ITextResult> completions, CancellationToken cancellationToken = default)
    {
        // To avoid any unexpected behavior we only take the first completion result (when running from the Kernel)
        return await completions[0].GetCompletionAsync(cancellationToken).ConfigureAwait(false);
    }

    internal SKFunction(
        Func<ITextCompletion?, CompleteRequestSettings?, SKContext, Task<SKContext>> delegateFunction,
        IList<ParameterView> parameters,
        string skillName,
        string functionName,
        string description,
        bool isSemantic = false,
        bool isSensitive = false,
        ITrustService? trustService = null,
        ILogger? log = null)
    {
        Verify.NotNull(delegateFunction);
        Verify.ValidSkillName(skillName);
        Verify.ValidFunctionName(functionName);
        Verify.ParametersUniqueness(parameters);

        this._log = log ?? NullLogger.Instance;

        // If no trust service is specified, use the default implementation
        this._trustService = trustService ?? TrustService.DefaultTrusted;

        this._function = delegateFunction;
        this.Parameters = parameters;

        this.IsSemantic = isSemantic;
        this.IsSensitive = isSensitive;
        this.Name = functionName;
        this.SkillName = skillName;
        this.Description = description;
    }

    /// <summary>
    /// Throw an exception if the function is not semantic, use this method when some logic makes sense only for semantic functions.
    /// </summary>
    /// <exception cref="KernelException"></exception>
    private void VerifyIsSemantic()
    {
        if (this.IsSemantic) { return; }

        this._log.LogError("The function is not semantic");
        throw new KernelException(
            KernelException.ErrorCodes.InvalidFunctionType,
            "Invalid operation, the method requires a semantic function");
    }

    private static MethodDetails GetMethodDetails(
        MethodInfo method,
        object? target,
        ILogger? log = null)
    {
        Verify.NotNull(method);

        // Get the name to use for the function.  If the function has an SKName attribute, we use that.
        // Otherwise, we use the name of the method, but strip off any "Async" suffix if it's {Value}Task-returning.
        // We don't apply any heuristics to the value supplied by SKName so that it can always be used
        // as a definitive override.
        string? functionName = method.GetCustomAttribute<SKNameAttribute>(inherit: true)?.Name?.Trim();
        functionName ??= method.GetCustomAttribute<SKFunctionNameAttribute>(inherit: true)?.Name?.Trim(); // TODO: SKFunctionName is deprecated. Remove.
        if (string.IsNullOrEmpty(functionName))
        {
            functionName = SanitizeMetadataName(method.Name!);
            Verify.ValidFunctionName(functionName);

            if (IsAsyncMethod(method) &&
                functionName.EndsWith("Async", StringComparison.Ordinal) &&
                functionName.Length > "Async".Length)
            {
                functionName = functionName.Substring(0, functionName.Length - "Async".Length);
            }
        }

        SKFunctionAttribute? functionAttribute = method.GetCustomAttribute<SKFunctionAttribute>(inherit: true);

        string? description = method.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description;
        description ??= functionAttribute?.Description; // TODO: SKFunctionAttribute.Description is deprecated. Remove.

        var result = new MethodDetails
        {
            Name = functionName!,
            Description = description ?? string.Empty,
            IsSensitive = functionAttribute?.IsSensitive ?? false,
        };

        (result.Function, result.Parameters) = GetDelegateInfo(target, method);

        log?.LogTrace("Method '{0}' found", result.Name);

        return result;
    }

    /// <summary>Gets whether a method has a known async return type.</summary>
    private static bool IsAsyncMethod(MethodInfo method)
    {
        Type t = method.ReturnType;

        if (t == typeof(Task) || t == typeof(ValueTask))
        {
            return true;
        }

        if (t.IsGenericType)
        {
            t = t.GetGenericTypeDefinition();
            if (t == typeof(Task<>) || t == typeof(ValueTask<>))
            {
                return true;
            }
        }

        return false;
    }

    // Inspect a method and returns the corresponding delegate and related info
    private static (Func<ITextCompletion?, CompleteRequestSettings?, SKContext, Task<SKContext>> function, List<ParameterView>) GetDelegateInfo(object? instance, MethodInfo method)
    {
        ThrowForInvalidSignatureIf(method.IsGenericMethodDefinition, method, "Generic methods are not supported");

        var stringParameterViews = new List<ParameterView>();
        var parameters = method.GetParameters();

        // Get marshaling funcs for parameters and build up the parameter views.
        var parameterFuncs = new Func<SKContext, object?>[parameters.Length];
        bool sawFirstParameter = false, hasSKContextParam = false, hasCancellationTokenParam = false, hasLoggerParam = false, hasMemoryParam = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            (parameterFuncs[i], ParameterView? parameterView) = GetParameterMarshalerDelegate(
                method, parameters[i],
                ref sawFirstParameter, ref hasSKContextParam, ref hasCancellationTokenParam, ref hasLoggerParam, ref hasMemoryParam);
            if (parameterView is not null)
            {
                stringParameterViews.Add(parameterView);
            }
        }

        // Get marshaling func for the return value.
        Func<object?, SKContext, Task<SKContext>> returnFunc = GetReturnValueMarshalerDelegate(method);

        // Create the func
        Func<ITextCompletion?, CompleteRequestSettings?, SKContext, Task<SKContext>> function = (_, _, context) =>
        {
            // Create the arguments.
            object?[] args = parameterFuncs.Length != 0 ? new object?[parameterFuncs.Length] : Array.Empty<object?>();
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = parameterFuncs[i](context);
            }

            // Invoke the method.
            object? result = method.Invoke(instance, args);

            // Extract and return the result.
            return returnFunc(result, context);
        };

        // Add parameters applied to the method that aren't part of the signature.
        stringParameterViews.AddRange(method
            .GetCustomAttributes<SKParameterAttribute>(inherit: true)
            .Select(x => new ParameterView(x.Name ?? string.Empty, x.Description ?? string.Empty, x.DefaultValue ?? string.Empty)));
        stringParameterViews.AddRange(method
            .GetCustomAttributes<SKFunctionContextParameterAttribute>(inherit: true)
            .Select(x => x.ToParameterView())); // TODO: SKFunctionContextParameterAttribute is deprecated. Remove.

        // Check for param names conflict
        Verify.ParametersUniqueness(stringParameterViews);

        // Return the function and its parameter views.
        return (function, stringParameterViews);
    }

    /// <summary>
    /// Gets a delegate for handling the marshaling of a parameter.
    /// </summary>
    private static (Func<SKContext, object?>, ParameterView?) GetParameterMarshalerDelegate(
        MethodInfo method, ParameterInfo parameter,
        ref bool sawFirstParameter, ref bool hasSKContextParam, ref bool hasCancellationTokenParam, ref bool hasLoggerParam, ref bool hasMemoryParam)
    {
        Type type = parameter.ParameterType;

        // Handle special types based on SKContext data. These can each show up at most once in the method signature,
        // with the SKContext itself or the primary data from it mapped directly into the method's parameter.
        // They do not get parameter views as they're not supplied from context variables.

        if (type == typeof(SKContext))
        {
            TrackUniqueParameterType(ref hasSKContextParam, method, $"At most one {nameof(SKContext)} parameter is permitted.");
            return (static (SKContext ctx) => ctx, null);
        }

        if (type == typeof(ISemanticTextMemory))
        {
            TrackUniqueParameterType(ref hasMemoryParam, method, $"At most one {nameof(ISemanticTextMemory)} parameter is permitted.");
            return (static (SKContext ctx) => ctx.Memory, null);
        }

        if (type == typeof(ILogger))
        {
            TrackUniqueParameterType(ref hasLoggerParam, method, $"At most one {nameof(ILogger)} parameter is permitted.");
            return (static (SKContext ctx) => ctx.Log, null);
        }

        if (type == typeof(CancellationToken))
        {
            TrackUniqueParameterType(ref hasCancellationTokenParam, method, $"At most one {nameof(CancellationToken)} parameter is permitted.");
            return (static (SKContext ctx) => ctx.CancellationToken, null);
        }

        // Handle context variables. These are supplied from the SKContext's Variables dictionary.

        if (!type.IsByRef && GetParser(type) is Func<string, CultureInfo?, object> parser)
        {
            // Use either the parameter's name or an override from an applied SKName attribute.
            SKNameAttribute? nameAttr = parameter.GetCustomAttribute<SKNameAttribute>(inherit: true);
            string name = nameAttr?.Name?.Trim() ?? SanitizeMetadataName(parameter.Name);
            bool nameIsInput = name.Equals("input", StringComparison.OrdinalIgnoreCase);
            ThrowForInvalidSignatureIf(name.Length == 0, method, $"Parameter {parameter.Name}'s context attribute defines an invalid name.");
            ThrowForInvalidSignatureIf(sawFirstParameter && nameIsInput, method, "Only the first parameter may be named 'input'");

            // TODO: Remove this if block for SKFunctionInputAttribute. It's deprecated.
            if (!sawFirstParameter &&
                method.GetCustomAttribute<SKFunctionInputAttribute>(inherit: true) is SKFunctionInputAttribute inputAttr)
            {
                sawFirstParameter = true;
                return (static (SKContext ctx) => ctx.Variables.Input.Value, inputAttr.ToParameterView());
            }

            // Use either the parameter's optional default value as contained in parameter metadata (e.g. `string s = "hello"`)
            // or an override from an applied SKParameter attribute. Note that a default value may be null.
            DefaultValueAttribute defaultValueAttribute = parameter.GetCustomAttribute<DefaultValueAttribute>(inherit: true);
            bool hasDefaultValue = defaultValueAttribute is not null;
            object? defaultValue = defaultValueAttribute?.Value;
            if (!hasDefaultValue && parameter.HasDefaultValue)
            {
                hasDefaultValue = true;
                defaultValue = parameter.DefaultValue;
            }

            if (hasDefaultValue)
            {
                // If we got a default value, make sure it's of the right type. This currently supports
                // null values if the target type is a reference type or a Nullable<T>, strings,
                // anything that can be parsed from a string via a registered TypeConverter,
                // and a value that's already the same type as the parameter.
                if (defaultValue is string defaultStringValue && defaultValue.GetType() != typeof(string))
                {
                    // Invariant culture is used here as this value comes from the C# source
                    // and it should be deterministic across cultures.
                    defaultValue = parser(defaultStringValue, CultureInfo.InvariantCulture);
                }
                else
                {
                    ThrowForInvalidSignatureIf(
                        defaultValue is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null,
                        method,
                        $"Type {type} is a non-nullable value type but a null default value was specified.");
                    ThrowForInvalidSignatureIf(
                        defaultValue is not null && !type.IsAssignableFrom(defaultValue.GetType()),
                        method,
                        $"Default value {defaultValue} for parameter {name} is not assignable to type {type}.");
                }
            }

            bool fallBackToInput = !sawFirstParameter && !nameIsInput;
            Func<SKContext, object?> parameterFunc = (SKContext ctx) =>
            {
                // 1. Use the value of the variable if it exists.
                if (ctx.Variables.Get(name, out string value))
                {
                    return Process(value);
                }

                // 2. Otherwise, use the default value if there is one, sourced either from an attribute or the parameter's default.
                if (hasDefaultValue)
                {
                    return defaultValue;
                }

                // 3. Otherwise, use "input" if this is the first (or only) parameter.
                if (fallBackToInput)
                {
                    return Process(ctx.Variables.Input.Value);
                }

                // 4. Otherwise, fail.
                throw new KernelException(KernelException.ErrorCodes.FunctionInvokeError, $"Missing value for parameter '{name}'");

                object? Process(string value)
                {
                    if (type == typeof(string))
                    {
                        return value;
                    }

                    try
                    {
                        return parser(value, /*culture*/null);
                    }
                    catch (Exception e) when (!e.IsCriticalException())
                    {
                        throw new ArgumentOutOfRangeException(name, value, e.Message);
                    }
                }
            };

            sawFirstParameter = true;

            var parameterView = new ParameterView(
                name,
                parameter.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description ?? string.Empty,
                defaultValue?.ToString() ?? string.Empty);

            return (parameterFunc, parameterView);
        }

        // Fail for unknown parameter types.
        throw GetExceptionForInvalidSignature(method, $"Unknown parameter type {parameter.ParameterType}");
    }

    /// <summary>
    /// Gets a delegate for handling the result value of a method, converting it into the <see cref="Task{SKContext}"/> to return from the invocation.
    /// </summary>
    private static Func<object?, SKContext, Task<SKContext>> GetReturnValueMarshalerDelegate(MethodInfo method)
    {
        // Handle each known return type for the method
        Type returnType = method.ReturnType;

        // No return value, either synchronous (void) or asynchronous (Task / ValueTask).

        if (returnType == typeof(void))
        {
            return static (result, context) => Task.FromResult(context);
        }

        if (returnType == typeof(Task))
        {
            return async static (result, context) =>
            {
                await ((Task)ThrowIfNullResult(result)).ConfigureAwait(false);
                return context;
            };
        }

        if (returnType == typeof(ValueTask))
        {
            return async static (result, context) =>
            {
                await ((ValueTask)ThrowIfNullResult(result)).ConfigureAwait(false);
                return context;
            };
        }

        // SKContext, either synchronous (SKContext) or asynchronous (Task<SKContext> / ValueTask<SKContext>).

        if (returnType == typeof(SKContext))
        {
            return static (result, _) => Task.FromResult((SKContext)ThrowIfNullResult(result));
        }

        if (returnType == typeof(Task<SKContext>))
        {
            return static (result, _) => (Task<SKContext>)ThrowIfNullResult(result);
        }

        if (returnType == typeof(ValueTask<SKContext>))
        {
            return static (result, context) => ((ValueTask<SKContext>)ThrowIfNullResult(result)).AsTask();
        }

        // string (which is special as no marshaling is required), either synchronous (string) or asynchronous (Task<string> / ValueTask<string>)

        if (returnType == typeof(string))
        {
            return static (result, context) =>
            {
                context.Variables.UpdateKeepingTrustState((string?)result);
                return Task.FromResult(context);
            };
        }

        if (returnType == typeof(Task<string>))
        {
            return async static (result, context) =>
            {
                context.Variables.UpdateKeepingTrustState(await ((Task<string>)ThrowIfNullResult(result)).ConfigureAwait(false));
                return context;
            };
        }

        if (returnType == typeof(ValueTask<string>))
        {
            return async static (result, context) =>
            {
                context.Variables.UpdateKeepingTrustState(await ((ValueTask<string>)ThrowIfNullResult(result)).ConfigureAwait(false));
                return context;
            };
        }

        // All other synchronous return types T.

        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            if (GetFormatter(returnType) is not Func<object?, CultureInfo?, string> formatter)
            {
                throw GetExceptionForInvalidSignature(method, $"Unknown return type {returnType}");
            }

            return (result, context) =>
            {
                context.Variables.UpdateKeepingTrustState(formatter(result, /*culture*/null));
                return Task.FromResult(context);
            };
        }

        // All other asynchronous return types

        // Task<T>
        if (returnType.GetGenericTypeDefinition() is Type genericTask &&
            genericTask == typeof(Task<>) &&
            returnType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod() is MethodInfo taskResultGetter &&
            GetFormatter(taskResultGetter.ReturnType) is Func<object?, CultureInfo?, string> taskResultFormatter)
        {
            return async (result, context) =>
            {
                await ((Task)ThrowIfNullResult(result)).ConfigureAwait(false);
                context.Variables.UpdateKeepingTrustState(taskResultFormatter(taskResultGetter.Invoke(result!, Array.Empty<object>()), /*culture*/null));
                return context;
            };
        }

        // ValueTask<T>
        if (returnType.GetGenericTypeDefinition() is Type genericValueTask &&
            genericValueTask == typeof(ValueTask<>) &&
            returnType.GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance) is MethodInfo valueTaskAsTask &&
            valueTaskAsTask.ReturnType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod() is MethodInfo asTaskResultGetter &&
            GetFormatter(asTaskResultGetter.ReturnType) is Func<object?, CultureInfo?, string> asTaskResultFormatter)
        {
            return async (result, context) =>
            {
                Task task = (Task)valueTaskAsTask.Invoke(ThrowIfNullResult(result), Array.Empty<object>());
                await task.ConfigureAwait(false);
                context.Variables.Update(asTaskResultFormatter(asTaskResultGetter.Invoke(task!, Array.Empty<object>()), /*culture*/null));
                return context;
            };
        }

        // Unrecognized return type.
        throw GetExceptionForInvalidSignature(method, $"Unknown return type {returnType}");

        // Throws an exception if a result is found to be null unexpectedly
        static object ThrowIfNullResult(object? result) =>
            result ??
            throw new KernelException(KernelException.ErrorCodes.FunctionInvokeError, "Function returned null unexpectedly.");
    }

    /// <summary>Gets an exception that can be thrown indicating an invalid signature.</summary>
    [DoesNotReturn]
    private static Exception GetExceptionForInvalidSignature(MethodInfo method, string reason) =>
        throw new KernelException(
            KernelException.ErrorCodes.FunctionTypeNotSupported,
            $"Function '{method.Name}' is not supported by the kernel. {reason}");

    /// <summary>Throws an exception indicating an invalid SKFunction signature if the specified condition is not met.</summary>
    private static void ThrowForInvalidSignatureIf([DoesNotReturnIf(true)] bool condition, MethodInfo method, string reason)
    {
        if (condition)
        {
            throw GetExceptionForInvalidSignature(method, reason);
        }
    }

    /// <summary>Tracks whether a particular kind of parameter has been seen, throwing an exception if it has, and marking it as seen if it hasn't</summary>
    private static void TrackUniqueParameterType(ref bool hasParameterType, MethodInfo method, string failureMessage)
    {
        ThrowForInvalidSignatureIf(hasParameterType, method, failureMessage);
        hasParameterType = true;
    }

    /// <summary>
    /// Gets a TypeConverter-based parser for parsing a string as the target type.
    /// </summary>
    /// <param name="targetType">Specifies the target type into which a string should be parsed.</param>
    /// <returns>The parsing function if the target type is supported; otherwise, null.</returns>
    /// <remarks>
    /// The parsing function uses whatever TypeConverter is registered for the target type.
    /// Parsing is first attempted using the current culture, and if that fails, it tries again
    /// with the invariant culture. If both fail, an exception is thrown.
    /// </remarks>
    private static Func<string, CultureInfo?, object?>? GetParser(Type targetType) =>
        s_parsers.GetOrAdd(targetType, static targetType =>
        {
            // Strings just parse to themselves.
            if (targetType == typeof(string))
            {
                return (input, cultureInfo) => input;
            }

            // For nullables, parse as the inner type.  We then just need to be careful to treat null as null,
            // as the underlying parser might not be expecting null.
            bool wasNullable = false;
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                wasNullable = true;
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            // For enums, delegate to Enum.Parse, special-casing null if it was actually Nullable<EnumType>.
            if (targetType.IsEnum)
            {
                return (input, cultureInfo) =>
                {
                    if (wasNullable && input is null)
                    {
                        return null!;
                    }

                    return Enum.Parse(targetType, input, ignoreCase: true);
                };
            }

            // Finally, look up and use a type converter.  Again, special-case null if it was actually Nullable<T>.
            if (GetTypeConverter(targetType) is TypeConverter converter && converter.CanConvertFrom(typeof(string)))
            {
                return (input, cultureInfo) =>
                {
                    if (wasNullable && input is null)
                    {
                        return null!;
                    }

                    // First try to parse using the supplied culture (or current if none was supplied).
                    // If that fails, try with the invariant culture and allow any exception to propagate.
                    try
                    {
                        return converter.ConvertFromString(context: null, cultureInfo ?? CultureInfo.CurrentCulture, input);
                    }
                    catch (Exception e) when (!e.IsCriticalException() && cultureInfo != CultureInfo.InvariantCulture)
                    {
                        return converter.ConvertFromInvariantString(input);
                    }
                };
            }

            // Unsupported type.
            return null;
        });

    /// <summary>
    /// Gets a TypeConverter-based formatter for formatting an object as a string.
    /// </summary>
    /// <remarks>
    /// Formatting is performed in the invariant culture whenever possible.
    /// </remarks>
    private static Func<object?, CultureInfo?, string?>? GetFormatter(Type targetType) =>
        s_formatters.GetOrAdd(targetType, static targetType =>
        {
            // For nullables, render as the underlying type.
            bool wasNullable = false;
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                wasNullable = true;
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            // For enums, just ToString() and allow the object override to do the right thing.
            if (targetType.IsEnum)
            {
                return (input, cultureInfo) => input?.ToString()!;
            }

            // Strings just render as themselves.
            if (targetType == typeof(string))
            {
                return (input, cultureInfo) => (string)input!;
            }

            // Finally, look up and use a type converter.
            if (GetTypeConverter(targetType) is TypeConverter converter && converter.CanConvertTo(typeof(string)))
            {
                return (input, cultureInfo) =>
                {
                    if (wasNullable && input is null)
                    {
                        return null!;
                    }

                    return converter.ConvertToString(context: null, cultureInfo ?? CultureInfo.InvariantCulture, input);
                };
            }

            return null;
        });

    private static TypeConverter? GetTypeConverter(Type targetType)
    {
        // In an ideal world, this would use TypeDescriptor.GetConverter. However, that is not friendly to
        // any form of ahead-of-time compilation, as it could end up requiring functionality that was trimmed.
        // Instead, we just use a hard-coded set of converters for the types we know about and then also support
        // types that are explicitly attributed with TypeConverterAttribute.

        if (targetType == typeof(byte)) { return new ByteConverter(); }
        if (targetType == typeof(sbyte)) { return new SByteConverter(); }
        if (targetType == typeof(bool)) { return new BooleanConverter(); }
        if (targetType == typeof(ushort)) { return new UInt16Converter(); }
        if (targetType == typeof(short)) { return new Int16Converter(); }
        if (targetType == typeof(char)) { return new CharConverter(); }
        if (targetType == typeof(uint)) { return new UInt32Converter(); }
        if (targetType == typeof(int)) { return new Int32Converter(); }
        if (targetType == typeof(ulong)) { return new UInt64Converter(); }
        if (targetType == typeof(long)) { return new Int64Converter(); }
        if (targetType == typeof(float)) { return new SingleConverter(); }
        if (targetType == typeof(double)) { return new DoubleConverter(); }
        if (targetType == typeof(decimal)) { return new DecimalConverter(); }
        if (targetType == typeof(TimeSpan)) { return new TimeSpanConverter(); }
        if (targetType == typeof(DateTime)) { return new DateTimeConverter(); }
        if (targetType == typeof(DateTimeOffset)) { return new DateTimeOffsetConverter(); }
        if (targetType == typeof(Uri)) { return new UriTypeConverter(); }
        if (targetType == typeof(Guid)) { return new GuidConverter(); }

        if (targetType.GetCustomAttribute<TypeConverterAttribute>() is TypeConverterAttribute tca &&
            Type.GetType(tca.ConverterTypeName, throwOnError: false) is Type converterType &&
            Activator.CreateInstance(converterType) is TypeConverter converter)
        {
            return converter;
        }

        return null;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"{this.Name} ({this.Description})";

    /// <summary>
    /// Remove characters from method name that are valid in metadata but invalid for SK.
    /// </summary>
    private static string SanitizeMetadataName(string methodName) =>
        s_invalidNameCharsRegex.Replace(methodName, "_");

    /// <summary>Regex that flags any character other than ASCII digits or letters or the underscore.</summary>
    private static readonly Regex s_invalidNameCharsRegex = new("[^0-9A-Za-z_]");

    /// <summary>Parser functions for converting strings to parameter types.</summary>
    private static readonly ConcurrentDictionary<Type, Func<string, CultureInfo?, object>?> s_parsers = new();

    /// <summary>Formatter functions for converting parameter types to strings.</summary>
    private static readonly ConcurrentDictionary<Type, Func<object?, CultureInfo?, string>?> s_formatters = new();

    #endregion
}
