﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Security;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.Planning;

/// <summary>
/// Standard Semantic Kernel callable plan.
/// Plan is used to create trees of <see cref="ISKFunction"/>s.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Plan : ISKFunction
{
    /// <summary>
    /// State of the plan
    /// </summary>
    [JsonPropertyName("state")]
    [JsonConverter(typeof(ContextVariablesConverter))]
    public ContextVariables State { get; } = new();

    /// <summary>
    /// Steps of the plan
    /// </summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<Plan> Steps => this._steps.AsReadOnly();

    /// <summary>
    /// Parameters for the plan, used to pass information to the next step
    /// </summary>
    [JsonPropertyName("parameters")]
    [JsonConverter(typeof(ContextVariablesConverter))]
    public ContextVariables Parameters { get; set; } = new();

    /// <summary>
    /// Outputs for the plan, used to pass information to the caller
    /// </summary>
    [JsonPropertyName("outputs")]
    public IList<string> Outputs { get; set; } = new List<string>();

    /// <summary>
    /// Gets whether the plan has a next step.
    /// </summary>
    [JsonIgnore]
    public bool HasNextStep => this.NextStepIndex < this.Steps.Count;

    /// <summary>
    /// Gets the next step index.
    /// </summary>
    [JsonPropertyName("next_step_index")]
    public int NextStepIndex { get; private set; }

    #region ISKFunction implementation

    /// <inheritdoc/>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    [JsonPropertyName("skill_name")]
    public string SkillName { get; set; } = string.Empty;

    /// <inheritdoc/>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IsSemantic { get; private set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public bool IsSensitive { get; private set; } = false;

    /// <inheritdoc/>
    [JsonIgnore]
    public ITrustService TrustServiceInstance { get; private set; } = TrustService.DefaultTrusted;

    /// <inheritdoc/>
    [JsonIgnore]
    public CompleteRequestSettings RequestSettings { get; private set; } = new();

    #endregion ISKFunction implementation

    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class with a goal description.
    /// </summary>
    /// <param name="goal">The goal of the plan used as description.</param>
    public Plan(string goal)
    {
        this.Description = goal;
        this.SkillName = this.GetType().FullName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class with a goal description and steps.
    /// </summary>
    /// <param name="goal">The goal of the plan used as description.</param>
    /// <param name="steps">The steps to add.</param>
    public Plan(string goal, params ISKFunction[] steps) : this(goal)
    {
        this.AddSteps(steps);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class with a goal description and steps.
    /// </summary>
    /// <param name="goal">The goal of the plan used as description.</param>
    /// <param name="steps">The steps to add.</param>
    public Plan(string goal, params Plan[] steps) : this(goal)
    {
        this.AddSteps(steps);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class with a function.
    /// </summary>
    /// <param name="function">The function to execute.</param>
    public Plan(ISKFunction function)
    {
        this.SetFunction(function);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plan"/> class with a function and steps.
    /// </summary>
    /// <param name="name">The name of the plan.</param>
    /// <param name="skillName">The name of the skill.</param>
    /// <param name="description">The description of the plan.</param>
    /// <param name="nextStepIndex">The index of the next step.</param>
    /// <param name="state">The state of the plan.</param>
    /// <param name="parameters">The parameters of the plan.</param>
    /// <param name="outputs">The outputs of the plan.</param>
    /// <param name="steps">The steps of the plan.</param>
    [JsonConstructor]
    public Plan(
        string name,
        string skillName,
        string description,
        int nextStepIndex,
        ContextVariables state,
        ContextVariables parameters,
        IList<string> outputs,
        IReadOnlyList<Plan> steps)
    {
        this.Name = name;
        this.SkillName = skillName;
        this.Description = description;
        this.NextStepIndex = nextStepIndex;
        this.State = state;
        this.Parameters = parameters;
        this.Outputs = outputs;
        this._steps.Clear();
        this.AddSteps(steps.ToArray());
    }

    /// <summary>
    /// Deserialize a JSON string into a Plan object.
    /// TODO: the context should never be null, it's required internally
    /// </summary>
    /// <param name="json">JSON string representation of a Plan</param>
    /// <param name="context">The context to use for function registrations.</param>
    /// <returns>An instance of a Plan object.</returns>
    /// <remarks>If Context is not supplied, plan will not be able to execute.</remarks>
    public static Plan FromJson(string json, SKContext? context = null)
    {
        var plan = JsonSerializer.Deserialize<Plan>(json, new JsonSerializerOptions { IncludeFields = true }) ?? new Plan(string.Empty);

        if (context != null)
        {
            plan = SetAvailableFunctions(plan, context);
        }

        return plan;
    }

    /// <summary>
    /// Get JSON representation of the plan.
    /// </summary>
    /// <param name="indented">Whether to emit indented JSON</param>
    /// <returns>Plan serialized using JSON format</returns>
    public string ToJson(bool indented = false)
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>
    /// Adds one or more existing plans to the end of the current plan as steps.
    /// </summary>
    /// <param name="steps">The plans to add as steps to the current plan.</param>
    /// <remarks>
    /// When you add a plan as a step to the current plan, the steps of the added plan are executed after the steps of the current plan have completed.
    /// </remarks>
    public void AddSteps(params Plan[] steps)
    {
        this._steps.AddRange(steps);
    }

    /// <summary>
    /// Adds one or more new steps to the end of the current plan.
    /// </summary>
    /// <param name="steps">The steps to add to the current plan.</param>
    /// <remarks>
    /// When you add a new step to the current plan, it is executed after the previous step in the plan has completed. Each step can be a function call or another plan.
    /// </remarks>
    public void AddSteps(params ISKFunction[] steps)
    {
        this._steps.AddRange(steps.Select(step => new Plan(step)));
    }

    /// <summary>
    /// Runs the next step in the plan using the provided kernel instance and variables.
    /// </summary>
    /// <param name="kernel">The kernel instance to use for executing the plan.</param>
    /// <param name="variables">The variables to use for the execution of the plan.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the execution of the plan.</param>
    /// <returns>A task representing the asynchronous execution of the plan's next step.</returns>
    /// <remarks>
    /// This method executes the next step in the plan using the specified kernel instance and context variables. The context variables contain the necessary information for executing the plan, such as the memory, skills, and logger. The method returns a task representing the asynchronous execution of the plan's next step.
    /// </remarks>
    public Task<Plan> RunNextStepAsync(IKernel kernel, ContextVariables variables, CancellationToken cancellationToken = default)
    {
        var context = new SKContext(
            variables,
            kernel.Memory,
            kernel.Skills,
            kernel.Log,
            cancellationToken);
        return this.InvokeNextStepAsync(context);
    }

    /// <summary>
    /// Invoke the next step of the plan
    /// </summary>
    /// <param name="context">Context to use</param>
    /// <returns>The updated plan</returns>
    /// <exception cref="KernelException">If an error occurs while running the plan</exception>
    public async Task<Plan> InvokeNextStepAsync(SKContext context)
    {
        if (this.HasNextStep)
        {
            var step = this.Steps[this.NextStepIndex];

            // Merge the state with the current context variables for step execution
            var functionVariables = this.GetNextStepVariables(context.Variables, step);

            // Execute the step
            var functionContext = new SKContext(functionVariables, context.Memory, context.Skills, context.Log, context.CancellationToken);
            var result = await step.InvokeAsync(functionContext).ConfigureAwait(false);
            var resultValue = result.Result.Trim();

            if (result.ErrorOccurred)
            {
                throw new KernelException(KernelException.ErrorCodes.FunctionInvokeError,
                    $"Error occurred while running plan step: {result.LastErrorDescription}", result.LastException);
            }

            #region Update State

            // Update state with result
            this.State.Update(resultValue);

            // Update Plan Result in State with matching outputs (if any)
            if (this.Outputs.Intersect(step.Outputs).Any())
            {
                this.State.TryGetValue(DefaultResultKey, out string? currentPlanResult);
                this.State.Set(DefaultResultKey, string.Join("\n", currentPlanResult?.Trim(), resultValue));
            }

            // Update state with outputs (if any)
            foreach (var item in step.Outputs)
            {
                if (result.Variables.TryGetValue(item, out string? val))
                {
                    this.State.Set(item, val);
                }
                else
                {
                    this.State.Set(item, resultValue);
                }
            }

            #endregion Update State

            this.NextStepIndex++;
        }

        return this;
    }

    #region ISKFunction implementation

    /// <inheritdoc/>
    public FunctionView Describe()
    {
        // TODO - Eventually, we should be able to describe a plan and its expected inputs/outputs
        return this.Function?.Describe() ?? new();
    }

    /// <inheritdoc/>
    public Task<SKContext> InvokeAsync(
        string? input = null,
        CompleteRequestSettings? settings = null,
        ISemanticTextMemory? memory = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (input != null) { this.State.Update(input); }

        SKContext context = new(
            this.State,
            memory: memory,
            logger: logger,
            cancellationToken: cancellationToken);

        return this.InvokeAsync(context, settings);
    }

    /// <inheritdoc/>
    public async Task<SKContext> InvokeAsync(
        SKContext context,
        CompleteRequestSettings? settings = null)
    {
        if (this.Function is not null)
        {
            var result = await this.Function.InvokeAsync(context, settings).ConfigureAwait(false);

            if (result.ErrorOccurred)
            {
                result.Log.LogError(
                    result.LastException,
                    "Something went wrong in plan step {0}.{1}:'{2}'", this.SkillName, this.Name, context.LastErrorDescription);
                return result;
            }

            context.Variables.Update(result.Result);
        }
        else
        {
            // loop through steps and execute until completion
            while (this.HasNextStep)
            {
                var functionContext = context;

                AddVariablesToContext(this.State, functionContext);

                await this.InvokeNextStepAsync(functionContext).ConfigureAwait(false);

                this.UpdateContextWithOutputs(context);
            }
        }

        return context;
    }

    /// <inheritdoc/>
    public ISKFunction SetDefaultSkillCollection(IReadOnlySkillCollection skills)
    {
        return this.Function is null
            ? throw new NotImplementedException()
            : this.Function.SetDefaultSkillCollection(skills);
    }

    /// <inheritdoc/>
    public ISKFunction SetAIService(Func<ITextCompletion> serviceFactory)
    {
        return this.Function is null
            ? throw new NotImplementedException()
            : this.Function.SetAIService(serviceFactory);
    }

    /// <inheritdoc/>
    public ISKFunction SetAIConfiguration(CompleteRequestSettings settings)
    {
        return this.Function is null
            ? throw new NotImplementedException()
            : this.Function.SetAIConfiguration(settings);
    }

    #endregion ISKFunction implementation

    /// <summary>
    /// Expand variables in the input string.
    /// </summary>
    /// <param name="variables">Variables to use for expansion.</param>
    /// <param name="input">Input string to expand.</param>
    /// <returns>Expanded string.</returns>
    internal string ExpandFromVariables(ContextVariables variables, string input)
    {
        var result = input;
        var matches = s_variablesRegex.Matches(input);
        var orderedMatches = matches.Cast<Match>().Select(m => m.Groups["var"].Value).Distinct().OrderByDescending(m => m.Length);

        foreach (var varName in orderedMatches)
        {
            if (variables.TryGetValue(varName, out string? value) || this.State.TryGetValue(varName, out value))
            {
                result = result.Replace($"${varName}", value);
            }
        }

        return result;
    }

    /// <summary>
    /// Set functions for a plan and its steps.
    /// </summary>
    /// <param name="plan">Plan to set functions for.</param>
    /// <param name="context">Context to use.</param>
    /// <returns>The plan with functions set.</returns>
    private static Plan SetAvailableFunctions(Plan plan, SKContext context)
    {
        if (plan.Steps.Count == 0)
        {
            if (context.Skills == null)
            {
                throw new KernelException(
                    KernelException.ErrorCodes.SkillCollectionNotSet,
                    "Skill collection not found in the context");
            }

            if (context.Skills.TryGetFunction(plan.SkillName, plan.Name, out var skillFunction))
            {
                plan.SetFunction(skillFunction);
            }
        }
        else
        {
            foreach (var step in plan.Steps)
            {
                SetAvailableFunctions(step, context);
            }
        }

        return plan;
    }

    /// <summary>
    /// Add any missing variables from a plan state variables to the context.
    /// </summary>
    private static void AddVariablesToContext(ContextVariables vars, SKContext context)
    {
        // Loop through vars and add anything missing to context
        foreach (var item in vars)
        {
            if (!context.Variables.ContainsKey(item.Key))
            {
                context.Variables.Set(item.Key, item.Value);
            }
        }
    }

    /// <summary>
    /// Update the context with the outputs from the current step.
    /// </summary>
    /// <param name="context">The context to update.</param>
    /// <returns>The updated context.</returns>
    private SKContext UpdateContextWithOutputs(SKContext context)
    {
        var resultString = this.State.TryGetValue(DefaultResultKey, out string? result) ? result : this.State.ToString();
        context.Variables.Update(resultString);

        // copy previous step's variables to the next step
        foreach (var item in this._steps[this.NextStepIndex - 1].Outputs)
        {
            if (this.State.TryGetValue(item, out string? val))
            {
                context.Variables.Set(item, val);
            }
            else
            {
                context.Variables.Set(item, resultString);
            }
        }

        return context;
    }

    /// <summary>
    /// Get the variables for the next step in the plan.
    /// </summary>
    /// <param name="variables">The current context variables.</param>
    /// <param name="step">The next step in the plan.</param>
    /// <returns>The context variables for the next step in the plan.</returns>
    private ContextVariables GetNextStepVariables(ContextVariables variables, Plan step)
    {
        // Priority for Input
        // - Parameters (expand from variables if needed)
        // - SKContext.Variables
        // - Plan.State
        // - Empty if sending to another plan
        // - Plan.Description

        var input = string.Empty;
        if (!string.IsNullOrEmpty(step.Parameters.Input))
        {
            input = this.ExpandFromVariables(variables, step.Parameters.Input);
        }
        else if (!string.IsNullOrEmpty(variables.Input))
        {
            input = variables.Input;
        }
        else if (!string.IsNullOrEmpty(this.State.Input))
        {
            input = this.State.Input;
        }
        else if (step.Steps.Count > 0)
        {
            input = string.Empty;
        }
        else if (!string.IsNullOrEmpty(this.Description))
        {
            input = this.Description;
        }

        var stepVariables = new ContextVariables(input);

        // Priority for remaining stepVariables is:
        // - Function Parameters (pull from variables or state by a key value)
        // - Step Parameters (pull from variables or state by a key value)
        // - All other variables. These are carried over in case the function wants access to the ambient content.
        var functionParameters = step.Describe();
        foreach (var param in functionParameters.Parameters)
        {
            if (param.Name.Equals(ContextVariables.MainKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (variables.TryGetValue(param.Name, out string? value))
            {
                stepVariables.Set(param.Name, value);
            }
            else if (this.State.TryGetValue(param.Name, out value) && !string.IsNullOrEmpty(value))
            {
                stepVariables.Set(param.Name, value);
            }
        }

        foreach (var item in step.Parameters)
        {
            // Don't overwrite variable values that are already set
            if (stepVariables.ContainsKey(item.Key))
            {
                continue;
            }

            var expandedValue = this.ExpandFromVariables(variables, item.Value);
            if (!expandedValue.Equals(item.Value, StringComparison.OrdinalIgnoreCase))
            {
                stepVariables.Set(item.Key, expandedValue);
            }
            else if (variables.TryGetValue(item.Key, out string? value))
            {
                stepVariables.Set(item.Key, value);
            }
            else if (this.State.TryGetValue(item.Key, out value))
            {
                stepVariables.Set(item.Key, value);
            }
            else
            {
                stepVariables.Set(item.Key, expandedValue);
            }
        }

        foreach (KeyValuePair<string, TrustAwareString> item in variables)
        {
            if (!stepVariables.ContainsKey(item.Key))
            {
                stepVariables.Set(item.Key, item.Value);
            }
        }

        return stepVariables;
    }

    private void SetFunction(ISKFunction function)
    {
        this.Function = function;
        this.Name = function.Name;
        this.SkillName = function.SkillName;
        this.Description = function.Description;
        this.IsSemantic = function.IsSemantic;
        this.IsSensitive = function.IsSensitive;
        this.TrustServiceInstance = function.TrustServiceInstance;
        this.RequestSettings = function.RequestSettings;
    }

    private ISKFunction? Function { get; set; } = null;

    private readonly List<Plan> _steps = new();

    private static readonly Regex s_variablesRegex = new(@"\$(?<var>\w+)");

    private const string DefaultResultKey = "PLAN.RESULT";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string display = this.Description;

            if (!string.IsNullOrWhiteSpace(this.Name))
            {
                display = $"{this.Name} ({display})";
            }

            if (this._steps.Count > 0)
            {
                display += $", Steps = {this._steps.Count}, NextStep = {this.NextStepIndex}";
            }

            return display;
        }
    }
}
