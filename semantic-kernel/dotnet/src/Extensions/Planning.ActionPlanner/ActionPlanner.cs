﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning.Action;
using Microsoft.SemanticKernel.SkillDefinition;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace - Using NS of Plan
namespace Microsoft.SemanticKernel.Planning;
#pragma warning restore IDE0130

/// <summary>
/// Action Planner allows to select one function out of many, to achieve a given goal.
/// The planner implement the Intent Detection pattern, uses the functions registered
/// in the kernel to see if there's a relevant one, providing instructions to call the
/// function and the rationale used to select it. The planner can also return
/// "no function" is nothing relevant is available.
/// The rationale is currently available only in the prompt, we might include it in
/// the Plan object in future.
/// </summary>
public sealed class ActionPlanner
{
    private const string StopSequence = "#END-OF-PLAN";
    private const string SkillName = "this";

    // Planner semantic function
    private readonly ISKFunction _plannerFunction;

    // Context used to access the list of functions in the kernel
    private readonly SKContext _context;
    private readonly IKernel _kernel;
    private readonly ILogger _logger;

    // TODO: allow to inject skill store
    /// <summary>
    /// Initialize a new instance of the <see cref="ActionPlanner"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel instance.</param>
    /// <param name="prompt">Optional prompt override</param>
    /// <param name="logger">Optional logger</param>
    public ActionPlanner(
        IKernel kernel,
        string? prompt = null,
        ILogger? logger = null)
    {
        Verify.NotNull(kernel);

        this._logger = logger ?? new NullLogger<ActionPlanner>();

        string promptTemplate = prompt ?? EmbeddedResource.Read("skprompt.txt");

        this._plannerFunction = kernel.CreateSemanticFunction(
            skillName: SkillName,
            promptTemplate: promptTemplate,
            maxTokens: 1024,
            stopSequences: new[] { StopSequence });

        kernel.ImportSkill(this, skillName: SkillName);

        this._kernel = kernel;
        this._context = kernel.CreateNewContext();
    }

    public async Task<Plan> CreatePlanAsync(string goal)
    {
        if (string.IsNullOrEmpty(goal))
        {
            throw new PlanningException(PlanningException.ErrorCodes.InvalidGoal, "The goal specified is empty");
        }

        this._context.Variables.Update(goal);

        SKContext result = await this._plannerFunction.InvokeAsync(this._context).ConfigureAwait(false);

        ActionPlanResponse? planData;
        try
        {
            planData = JsonSerializer.Deserialize<ActionPlanResponse?>(result.ToString(), new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                DictionaryKeyPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (Exception e)
        {
            throw new PlanningException(PlanningException.ErrorCodes.InvalidPlan,
                "Plan parsing error, invalid JSON", e);
        }

        if (planData == null)
        {
            throw new PlanningException(PlanningException.ErrorCodes.InvalidPlan, "The plan deserialized to a null object");
        }

        // Build and return plan
        Plan plan;
        if (planData.Plan.Function.Contains("."))
        {
            var parts = planData.Plan.Function.Split('.');
            plan = new Plan(goal, this._context.Skills!.GetFunction(parts[0], parts[1]));
        }
        else if (!string.IsNullOrWhiteSpace(planData.Plan.Function))
        {
            plan = new Plan(goal, this._context.Skills!.GetFunction(planData.Plan.Function));
        }
        else
        {
            // No function was found - return a plan with no steps.
            plan = new Plan(goal);
        }

        // Create a plan using the function and the parameters suggested by the planner
        foreach (KeyValuePair<string, object> p in planData.Plan.Parameters)
        {
            if (p.Value != null)
            {
                plan.Parameters[p.Key] = p.Value.ToString();
            }
        }

        return plan;
    }

    // TODO: use goal to find relevant functions in a skill store
    /// <summary>
    /// Native function returning a list of all the functions in the current context,
    /// excluding functions in the planner itself.
    /// </summary>
    /// <param name="goal">Currently unused. Will be used to handle long lists of functions.</param>
    /// <param name="context">Function execution context</param>
    /// <returns>List of functions, formatted accordingly to the prompt</returns>
    [SKFunction, Description("List all functions available in the kernel")]
    public string ListOfFunctions(
        [Description("The current goal processed by the planner")] string goal,
        SKContext context)
    {
        Verify.NotNull(context.Skills);
        var functionsAvailable = context.Skills.GetFunctionsView();

        // Prepare list using the format used by skprompt.txt
        var list = new StringBuilder();
        this.PopulateList(list, functionsAvailable.NativeFunctions);
        this.PopulateList(list, functionsAvailable.SemanticFunctions);

        return list.ToString();
    }

    // TODO: generate string programmatically
    // TODO: use goal to find relevant examples
    [SKFunction, Description("List a few good examples of plans to generate")]
    public string GoodExamples(
        [Description("The current goal processed by the planner")] string goal,
        SKContext context)
    {
        return @"
[EXAMPLE]
- List of functions:
// Read a file.
FileIOSkill.ReadAsync
Parameter ""path"": Source file.
// Write a file.
FileIOSkill.WriteAsync
Parameter ""path"": Destination file. (default value: sample.txt)
Parameter ""content"": File content.
// Get the current time.
TimeSkill.Time
No parameters.
// Makes a POST request to a uri.
HttpSkill.PostAsync
Parameter ""body"": The body of the request.
- End list of functions.
Goal: create a file called ""something.txt"".
{""plan"":{
""rationale"": ""the list contains a function that allows to create files"",
""function"": ""FileIOSkill.WriteAsync"",
""parameters"": {
""path"": ""something.txt"",
""content"": null
}}}
#END-OF-PLAN
";
    }

    // TODO: generate string programmatically
    [SKFunction, Description("List a few edge case examples of plans to handle")]
    public string EdgeCaseExamples(
        [Description("The current goal processed by the planner")] string goal,
        SKContext context)
    {
        return @"
[EXAMPLE]
- List of functions:
// Get the current time.
TimeSkill.Time
No parameters.
// Write a file.
FileIOSkill.WriteAsync
Parameter ""path"": Destination file. (default value: sample.txt)
Parameter ""content"": File content.
// Makes a POST request to a uri.
HttpSkill.PostAsync
Parameter ""body"": The body of the request.
// Read a file.
FileIOSkill.ReadAsync
Parameter ""path"": Source file.
- End list of functions.
Goal: tell me a joke.
{""plan"":{
""rationale"": ""the list does not contain functions to tell jokes or something funny"",
""function"": """",
""parameters"": {
}}}
#END-OF-PLAN
";
    }

    private void PopulateList(StringBuilder list, IDictionary<string, List<FunctionView>> functions)
    {
        foreach (KeyValuePair<string, List<FunctionView>> skill in functions)
        {
            // Skip this planner skills
            if (string.Equals(skill.Key, SkillName, StringComparison.OrdinalIgnoreCase)) { continue; }

            foreach (FunctionView func in skill.Value)
            {
                // Function description
                if (func.Description != null)
                {
                    list.AppendLine($"// {AddPeriod(func.Description)}");
                }
                else
                {
                    this._logger.LogWarning("{0}.{1} is missing a description", func.SkillName, func.Name);
                    list.AppendLine($"// Function {func.SkillName}.{func.Name}.");
                }

                // Function name
                list.AppendLine($"{func.SkillName}.{func.Name}");

                // Function parameters
                foreach (var p in func.Parameters)
                {
                    var description = string.IsNullOrEmpty(p.Description) ? p.Name : p.Description;
                    var defaultValueString = string.IsNullOrEmpty(p.DefaultValue) ? string.Empty : $" (default value: {p.DefaultValue})";
                    list.AppendLine($"Parameter \"{p.Name}\": {AddPeriod(description)} {defaultValueString}");
                }
            }
        }
    }

    private static string AddPeriod(string x)
    {
        return x.EndsWith(".", StringComparison.Ordinal) ? x : $"{x}.";
    }
}
