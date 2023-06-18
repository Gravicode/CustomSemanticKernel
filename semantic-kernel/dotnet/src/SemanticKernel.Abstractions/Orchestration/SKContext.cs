﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.Orchestration;

/// <summary>
/// Semantic Kernel context.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SKContext
{
    /// <summary>
    /// Print the processed input, aka the current data after any processing occurred.
    /// </summary>
    /// <returns>Processed input, aka result</returns>
    public string Result => this.Variables.ToString();

    /// <summary>
    /// Whether all the context variables are trusted or not.
    /// </summary>
    public bool IsTrusted => this.Variables.IsAllTrusted();

    /// <summary>
    /// Whether an error occurred while executing functions in the pipeline.
    /// </summary>
    public bool ErrorOccurred { get; private set; }

    /// <summary>
    /// Error details.
    /// </summary>
    public string LastErrorDescription { get; private set; } = string.Empty;

    /// <summary>
    /// When an error occurs, this is the most recent exception.
    /// </summary>
    public Exception? LastException { get; private set; }

    /// <summary>
    /// When a prompt is processed, aka the current data after any model results processing occurred.
    /// (One prompt can have multiple results).
    /// </summary>
    public IReadOnlyCollection<ModelResult> ModelResults { get; set; } = Array.Empty<ModelResult>();

    /// <summary>
    /// The token to monitor for cancellation requests.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Shortcut into user data, access variables by name
    /// </summary>
    /// <param name="name">Variable name</param>
    public string this[string name]
    {
        get => this.Variables[name];
        set => this.Variables[name] = value;
    }

    /// <summary>
    /// Call this method to signal when an error occurs.
    /// In the usual scenarios this is also how execution is stopped, e.g. to inform the user or take necessary steps.
    /// </summary>
    /// <param name="errorDescription">Error description</param>
    /// <param name="exception">If available, the exception occurred</param>
    /// <returns>The current instance</returns>
    public SKContext Fail(string errorDescription, Exception? exception = null)
    {
        this.ErrorOccurred = true;
        this.LastErrorDescription = errorDescription;
        this.LastException = exception;
        return this;
    }

    /// <summary>
    /// User variables
    /// </summary>
    public ContextVariables Variables { get; }

    /// <summary>
    /// Semantic memory
    /// </summary>
    public ISemanticTextMemory Memory { get; }

    /// <summary>
    /// Read only skills collection
    /// </summary>
    public IReadOnlySkillCollection? Skills { get; internal set; }

    /// <summary>
    /// Access registered functions by skill + name. Not case sensitive.
    /// The function might be native or semantic, it's up to the caller handling it.
    /// </summary>
    /// <param name="skillName">Skill name</param>
    /// <param name="functionName">Function name</param>
    /// <returns>Delegate to execute the function</returns>
    public ISKFunction Func(string skillName, string functionName)
    {
        if (this.Skills is null)
        {
            throw new KernelException(
                KernelException.ErrorCodes.SkillCollectionNotSet,
                "Skill collection not found in the context");
        }

        return this.Skills.GetFunction(skillName, functionName);
    }

    /// <summary>
    /// App logger
    /// </summary>
    public ILogger Log { get; }

    /// <summary>
    /// Constructor for the context.
    /// </summary>
    /// <param name="variables">Context variables to include in context.</param>
    /// <param name="memory">Semantic text memory unit to include in context.</param>
    /// <param name="skills">Skills to include in context.</param>
    /// <param name="logger">Logger for operations in context.</param>
    /// <param name="cancellationToken">Optional cancellation token for operations in context.</param>
    public SKContext(
        ContextVariables? variables = null,
        ISemanticTextMemory? memory = null,
        IReadOnlySkillCollection? skills = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        this.Variables = variables ?? new();
        this.Memory = memory ?? NullMemory.Instance;
        this.Skills = skills ?? NullReadOnlySkillCollection.Instance;
        this.Log = logger ?? NullLogger.Instance;
        this.CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Make all the variables stored in the context untrusted.
    /// </summary>
    public void UntrustAll()
    {
        this.Variables.UntrustAll();
    }

    /// <summary>
    /// Make the result untrusted.
    /// </summary>
    public void UntrustResult()
    {
        this.Variables.UntrustInput();
    }

    /// <summary>
    /// Print the processed input, aka the current data after any processing occurred.
    /// If an error occurred, prints the last exception message instead.
    /// </summary>
    /// <returns>Processed input, aka result, or last exception message if any</returns>
    public override string ToString()
    {
        return this.ErrorOccurred ? $"Error: {this.LastErrorDescription}" : this.Result;
    }

    /// <summary>
    /// Create a clone of the current context, using the same kernel references (memory, skills, logger)
    /// and a new set variables, so that variables can be modified without affecting the original context.
    /// </summary>
    /// <returns>A new context copied from the current one</returns>
    public SKContext Clone()
    {
        return new SKContext(
            variables: this.Variables.Clone(),
            memory: this.Memory,
            skills: this.Skills,
            logger: this.Log,
            cancellationToken: this.CancellationToken)
        {
            ErrorOccurred = this.ErrorOccurred,
            LastErrorDescription = this.LastErrorDescription,
            LastException = this.LastException
        };
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            if (this.ErrorOccurred)
            {
                return $"Error: {this.LastErrorDescription}";
            }

            string display = this.Variables.DebuggerDisplay;

            if (this.Skills is IReadOnlySkillCollection skills)
            {
                var view = skills.GetFunctionsView();
                display += $", Skills = {view.NativeFunctions.Count + view.SemanticFunctions.Count}";
            }

            if (this.Memory is ISemanticTextMemory memory && memory is not NullMemory)
            {
                display += $", Memory = {memory.GetType().Name}";
            }

            return display;
        }
    }
}
