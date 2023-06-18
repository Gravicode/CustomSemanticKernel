﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Skills.Web;
using Microsoft.SemanticKernel.Skills.Web.Bing;
using Microsoft.SemanticKernel.Skills.Web.Google;
using Microsoft.SemanticKernel.TemplateEngine;
using RepoUtils;

/// <summary>
/// The example shows how to use Bing and Google to search for current data
/// you might want to import into your system, e.g. providing AI prompts with
/// recent information, or for AI to generate recent information to display to users.
/// </summary>
// ReSharper disable CommentTypo
// ReSharper disable once InconsistentNaming
public static class Example07_BingAndGoogleSkills
{
    public static async Task RunAsync()
    {
        IKernel kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithOpenAITextCompletionService("text-davinci-003", Env.Var("OPENAI_API_KEY"))
            .Build();

        // Load Bing skill
        using var bingConnector = new BingConnector(Env.Var("BING_API_KEY"));
        kernel.ImportSkill(new WebSearchEngineSkill(bingConnector), "bing");

        // Load Google skill
        using var googleConnector = new GoogleConnector(Env.Var("GOOGLE_API_KEY"), Env.Var("GOOGLE_SEARCH_ENGINE_ID"));
        kernel.ImportSkill(new WebSearchEngineSkill(googleConnector), "google");

        await Example1Async(kernel);
        await Example2Async(kernel);
    }

    private static async Task Example1Async(IKernel kernel)
    {
        Console.WriteLine("======== Bing and Google Search Skill ========");

        // Run
        var question = "What's the largest building in the world?";
        var bingResult = await kernel.Func("bing", "search").InvokeAsync(question);
        var googleResult = await kernel.Func("google", "search").InvokeAsync(question);

        Console.WriteLine(question);
        Console.WriteLine("----");
        Console.WriteLine(bingResult);
        Console.WriteLine("----");
        Console.WriteLine(googleResult);

        /* OUTPUT:

            What's the largest building in the world?
            ----
            The Aerium near Berlin, Germany is the largest uninterrupted volume in the world, while Boeing's
            factory in Everett, Washington, United States is the world's largest building by volume. The AvtoVAZ
            main assembly building in Tolyatti, Russia is the largest building in area footprint.
            ----
            The Aerium near Berlin, Germany is the largest uninterrupted volume in the world, while Boeing's
            factory in Everett, Washington, United States is the world's ...
       */
    }

    private static async Task Example2Async(IKernel kernel)
    {
        Console.WriteLine("======== Use Search Skill to answer user questions ========");

        const string SemanticFunction = @"Answer questions only when you know the facts or the information is provided.
When you don't have sufficient information you reply with a list of commands to find the information needed.
When answering multiple questions, use a bullet point list.
Note: make sure single and double quotes are escaped using a backslash char.

[COMMANDS AVAILABLE]
- bing.search

[INFORMATION PROVIDED]
{{ $externalInformation }}

[EXAMPLE 1]
Question: what's the biggest lake in Italy?
Answer: Lake Garda, also known as Lago di Garda.

[EXAMPLE 2]
Question: what's the biggest lake in Italy? What's the smallest positive number?
Answer:
* Lake Garda, also known as Lago di Garda.
* The smallest positive number is 1.

[EXAMPLE 3]
Question: what's Ferrari stock price ? Who is the current number one female tennis player in the world?
Answer:
{{ '{{' }} bing.search ""what\\'s Ferrari stock price?"" {{ '}}' }}.
{{ '{{' }} bing.search ""Who is the current number one female tennis player in the world?"" {{ '}}' }}.

[END OF EXAMPLES]

[TASK]
Question: {{ $input }}.
Answer: ";

        var questions = "Who is the most followed person on TikTok right now? What's the exchange rate EUR:USD?";
        Console.WriteLine(questions);

        var oracle = kernel.CreateSemanticFunction(SemanticFunction, maxTokens: 200, temperature: 0, topP: 1);

        var context = kernel.CreateNewContext();
        context["externalInformation"] = "";
        var answer = await oracle.InvokeAsync(questions, context);

        // If the answer contains commands, execute them using the prompt renderer.
        if (answer.Result.Contains("bing.search", StringComparison.OrdinalIgnoreCase))
        {
            var promptRenderer = new PromptTemplateEngine();

            Console.WriteLine("---- Fetching information from Bing...");
            var information = await promptRenderer.RenderAsync(answer.Result, context);

            Console.WriteLine("Information found:");
            Console.WriteLine(information);

            // The rendered prompt contains the information retrieved from search engines
            context["externalInformation"] = information;

            // Run the semantic function again, now including information from Bing
            answer = await oracle.InvokeAsync(questions, context);
        }
        else
        {
            Console.WriteLine("AI had all the information, no need to query Bing.");
        }

        Console.WriteLine("---- ANSWER:");
        Console.WriteLine(answer);

        /* OUTPUT:

            Who is the most followed person on TikTok right now? What's the exchange rate EUR:USD?
            ---- Fetching information from Bing...
            Information found:

            Khaby Lame is the most-followed user on TikTok. This list contains the top 50 accounts by number
            of followers on the Chinese social media platform TikTok, which was merged with musical.ly in 2018.
            [1] The most-followed individual on the platform is Khaby Lame, with over 153 million followers..
            EUR – Euro To USD – US Dollar 1.00 Euro = 1.10 37097 US Dollars 1 USD = 0.906035 EUR We use the
            mid-market rate for our Converter. This is for informational purposes only. You won’t receive this
            rate when sending money. Check send rates Convert Euro to US Dollar Convert US Dollar to Euro..
            ---- ANSWER:

            * The most followed person on TikTok right now is Khaby Lame, with over 153 million followers.
            * The exchange rate for EUR to USD is 1.1037097 US Dollars for 1 Euro.
         */
    }
}
