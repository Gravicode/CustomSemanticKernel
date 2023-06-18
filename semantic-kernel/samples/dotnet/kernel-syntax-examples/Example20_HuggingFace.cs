﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using RepoUtils;

/**
 * The following example shows how to use Semantic Kernel with HuggingFace API.
 */

// ReSharper disable once InconsistentNaming
public static class Example20_HuggingFace
{
    public static async Task RunAsync()
    {
        Console.WriteLine("======== HuggingFace text completion AI ========");

        IKernel kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithHuggingFaceTextCompletionService("gpt2", apiKey: Env.Var("HF_API_KEY"))
            .Build();

        const string FunctionDefinition = "Question: {{$input}}; Answer:";

        var questionAnswerFunction = kernel.CreateSemanticFunction(FunctionDefinition);

        var result = await questionAnswerFunction.InvokeAsync("What is New York?");

        Console.WriteLine(result);

        foreach (var modelResult in result.ModelResults)
        {
            Console.WriteLine(modelResult.GetHuggingFaceResult().AsJson());
        }
    }
}
