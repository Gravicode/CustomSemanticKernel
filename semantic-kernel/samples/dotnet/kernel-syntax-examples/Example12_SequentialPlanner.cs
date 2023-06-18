﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Planning.Sequential;
using RepoUtils;
using Skills;

// ReSharper disable CommentTypo
// ReSharper disable once InconsistentNaming
internal static class Example12_SequentialPlanner
{
    public static async Task RunAsync()
    {
        await PoetrySamplesAsync();
        await EmailSamplesAsync();
        await BookSamplesAsync();
        await MemorySampleAsync();
    }

    private static async Task PoetrySamplesAsync()
    {
        Console.WriteLine("======== Sequential Planner - Create and Execute Poetry Plan ========");
        var kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithAzureTextCompletionService(
                Env.Var("AZURE_OPENAI_DEPLOYMENT_NAME"),
                Env.Var("AZURE_OPENAI_ENDPOINT"),
                Env.Var("AZURE_OPENAI_KEY"))
            .Build();

        string folder = RepoFiles.SampleSkillsPath();
        kernel.ImportSemanticSkillFromDirectory(folder,
            "SummarizeSkill",
            "WriterSkill");

        var planner = new SequentialPlanner(kernel);

        var plan = await planner.CreatePlanAsync("Write a poem about John Doe, then translate it into Italian.");

        // Original plan:
        // Goal: Write a poem about John Doe, then translate it into Italian.

        // Steps:
        // - WriterSkill.ShortPoem INPUT='John Doe is a friendly guy who likes to help others and enjoys reading books.' =>
        // - WriterSkill.Translate language='Italian' INPUT='' =>

        Console.WriteLine("Original plan:");
        Console.WriteLine(plan.ToPlanString());

        var result = await kernel.RunAsync(plan);

        Console.WriteLine("Result:");
        Console.WriteLine(result.Result);
    }

    private static async Task EmailSamplesAsync()
    {
        Console.WriteLine("======== Sequential Planner - Create and Execute Email Plan ========");
        var kernel = InitializeKernelAndPlanner(out var planner, 512);
        kernel.ImportSkill(new EmailSkill(), "email");

        // Load additional skills to enable planner to do non-trivial asks.
        string folder = RepoFiles.SampleSkillsPath();
        kernel.ImportSemanticSkillFromDirectory(folder,
            "SummarizeSkill",
            "WriterSkill");

        var plan = await planner.CreatePlanAsync("Summarize an input, translate to french, and e-mail to John Doe");

        // Original plan:
        // Goal: Summarize an input, translate to french, and e-mail to John Doe

        // Steps:
        // - SummarizeSkill.Summarize INPUT='' =>
        // - WriterSkill.Translate language='French' INPUT='' => TRANSLATED_SUMMARY
        // - email.GetEmailAddress INPUT='John Doe' => EMAIL_ADDRESS
        // - email.SendEmail INPUT='$TRANSLATED_SUMMARY' email_address='$EMAIL_ADDRESS' =>

        Console.WriteLine("Original plan:");
        Console.WriteLine(plan.ToPlanString());

        var input =
            "Once upon a time, in a faraway kingdom, there lived a kind and just king named Arjun. " +
            "He ruled over his kingdom with fairness and compassion, earning him the love and admiration of his people. " +
            "However, the kingdom was plagued by a terrible dragon that lived in the nearby mountains and terrorized the nearby villages, " +
            "burning their crops and homes. The king had tried everything to defeat the dragon, but to no avail. " +
            "One day, a young woman named Mira approached the king and offered to help defeat the dragon. She was a skilled archer " +
            "and claimed that she had a plan to defeat the dragon once and for all. The king was skeptical, but desperate for a solution, " +
            "so he agreed to let her try. Mira set out for the dragon's lair and, with the help of her trusty bow and arrow, " +
            "she was able to strike the dragon with a single shot through its heart, killing it instantly. The people rejoiced " +
            "and the kingdom was at peace once again. The king was so grateful to Mira that he asked her to marry him and she agreed. " +
            "They ruled the kingdom together, ruling with fairness and compassion, just as Arjun had done before. They lived " +
            "happily ever after, with the people of the kingdom remembering Mira as the brave young woman who saved them from the dragon.";
        await ExecutePlanAsync(kernel, plan, input, 5);
    }

    private static async Task BookSamplesAsync()
    {
        Console.WriteLine("======== Sequential Planner - Create and Execute Book Creation Plan  ========");
        var kernel = InitializeKernelAndPlanner(out var planner);

        // Load additional skills to enable planner to do non-trivial asks.
        string folder = RepoFiles.SampleSkillsPath();
        kernel.ImportSemanticSkillFromDirectory(folder, "WriterSkill");
        kernel.ImportSemanticSkillFromDirectory(folder, "MiscSkill");

        var originalPlan = await planner.CreatePlanAsync("Create a book with 3 chapters about a group of kids in a club called 'The Thinking Caps.'");

        // Original plan:
        // Goal: Create a book with 3 chapters about a group of kids in a club called 'The Thinking Caps.'

        // Steps:
        // - WriterSkill.NovelOutline chapterCount='3' INPUT='A group of kids in a club called 'The Thinking Caps' that solve mysteries and puzzles using their creativity and logic.' endMarker='<!--===ENDPART===-->' => OUTLINE
        // - MiscSkill.ElementAtIndex count='3' INPUT='$OUTLINE' index='0' => CHAPTER_1_SYNOPSIS
        // - WriterSkill.NovelChapter chapterIndex='1' previousChapter='' INPUT='$CHAPTER_1_SYNOPSIS' theme='Children's mystery' => RESULT__CHAPTER_1
        // - MiscSkill.ElementAtIndex count='3' INPUT='$OUTLINE' index='1' => CHAPTER_2_SYNOPSIS
        // - WriterSkill.NovelChapter chapterIndex='2' previousChapter='$CHAPTER_1_SYNOPSIS' INPUT='$CHAPTER_2_SYNOPSIS' theme='Children's mystery' => RESULT__CHAPTER_2
        // - MiscSkill.ElementAtIndex count='3' INPUT='$OUTLINE' index='2' => CHAPTER_3_SYNOPSIS
        // - WriterSkill.NovelChapter chapterIndex='3' previousChapter='$CHAPTER_2_SYNOPSIS' INPUT='$CHAPTER_3_SYNOPSIS' theme='Children's mystery' => RESULT__CHAPTER_3

        Console.WriteLine("Original plan:");
        Console.WriteLine(originalPlan.ToPlanString());

        Stopwatch sw = new();
        sw.Start();
        await ExecutePlanAsync(kernel, originalPlan);
    }

    private static async Task MemorySampleAsync()
    {
        Console.WriteLine("======== Sequential Planner - Create and Execute Plan using Memory ========");

        var kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithAzureTextCompletionService(
                        Env.Var("AZURE_OPENAI_DEPLOYMENT_NAME"),
                        Env.Var("AZURE_OPENAI_ENDPOINT"),
                        Env.Var("AZURE_OPENAI_KEY"))
            .WithAzureTextEmbeddingGenerationService(
                        Env.Var("AZURE_OPENAI_EMBEDDINGS_DEPLOYMENT_NAME"),
                        Env.Var("AZURE_OPENAI_EMBEDDINGS_ENDPOINT"),
                        Env.Var("AZURE_OPENAI_EMBEDDINGS_KEY"))
            .WithMemoryStorage(new VolatileMemoryStore())
            .Build();

        string folder = RepoFiles.SampleSkillsPath();
        kernel.ImportSemanticSkillFromDirectory(folder,
            "SummarizeSkill",
            "WriterSkill",
            "CalendarSkill",
            "ChatSkill",
            "ChildrensBookSkill",
            "ClassificationSkill",
            "CodingSkill",
            "FunSkill",
            "IntentDetectionSkill",
            "MiscSkill",
            "QASkill");

        kernel.ImportSkill(new EmailSkill(), "email");
        kernel.ImportSkill(new StaticTextSkill(), "statictext");
        kernel.ImportSkill(new Microsoft.SemanticKernel.CoreSkills.TextSkill(), "coretext");

        var goal = "Create a book with 3 chapters about a group of kids in a club called 'The Thinking Caps.'";

        var planner = new SequentialPlanner(kernel, new SequentialPlannerConfig { RelevancyThreshold = 0.78 });

        var plan = await planner.CreatePlanAsync(goal);

        Console.WriteLine("Original plan:");
        Console.WriteLine(plan.ToPlanString());
    }

    private static IKernel InitializeKernelAndPlanner(out SequentialPlanner planner, int maxTokens = 1024)
    {
        var kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithAzureTextCompletionService(
                Env.Var("AZURE_OPENAI_DEPLOYMENT_NAME"),
                Env.Var("AZURE_OPENAI_ENDPOINT"),
                Env.Var("AZURE_OPENAI_KEY"))
            .Build();

        planner = new SequentialPlanner(kernel, new SequentialPlannerConfig { MaxTokens = maxTokens });

        return kernel;
    }

    private static async Task<Plan> ExecutePlanAsync(
        IKernel kernel,
        Plan plan,
        string input = "",
        int maxSteps = 10)
    {
        Stopwatch sw = new();
        sw.Start();

        // loop until complete or at most N steps
        try
        {
            for (int step = 1; plan.HasNextStep && step < maxSteps; step++)
            {
                if (string.IsNullOrEmpty(input))
                {
                    await plan.InvokeNextStepAsync(kernel.CreateNewContext());
                    // or await kernel.StepAsync(plan);
                }
                else
                {
                    plan = await kernel.StepAsync(input, plan);
                    input = string.Empty;
                }

                if (!plan.HasNextStep)
                {
                    Console.WriteLine($"Step {step} - COMPLETE!");
                    Console.WriteLine(plan.State.ToString());
                    break;
                }

                Console.WriteLine($"Step {step} - Results so far:");
                Console.WriteLine(plan.State.ToString());
            }
        }
        catch (KernelException e)
        {
            Console.WriteLine("Step - Execution failed:");
            Console.WriteLine(e.Message);
        }

        sw.Stop();
        Console.WriteLine($"Execution complete in {sw.ElapsedMilliseconds} ms!");
        return plan;
    }
}
