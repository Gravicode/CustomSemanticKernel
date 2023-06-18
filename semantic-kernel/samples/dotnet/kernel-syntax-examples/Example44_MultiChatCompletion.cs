﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI.ChatCompletion;
using RepoUtils;

/**
 * The following example shows how to use Semantic Kernel with Multiple Results Text Completion as streaming
 */
// ReSharper disable once InconsistentNaming
public static class Example44_MultiChatCompletion
{
    public static async Task RunAsync()
    {
        await AzureOpenAIMultiChatCompletionAsync();
        await OpenAIMultiChatCompletionAsync();
    }

    private static async Task AzureOpenAIMultiChatCompletionAsync()
    {
        Console.WriteLine("======== Azure OpenAI - Multiple Chat Completion ========");

        AzureChatCompletion azureChatCompletion = new(
            Env.Var("AZURE_OPENAI_CHAT_DEPLOYMENT_NAME"),
            Env.Var("AZURE_OPENAI_ENDPOINT"),
            Env.Var("AZURE_OPENAI_KEY"));

        await RunChatAsync(azureChatCompletion);
    }

    private static async Task OpenAIMultiChatCompletionAsync()
    {
        Console.WriteLine("======== Open AI - Multiple Chat Completion ========");

        OpenAIChatCompletion openAIChatCompletion = new("gpt-3.5-turbo", Env.Var("OPENAI_API_KEY"));

        await RunChatAsync(openAIChatCompletion);
    }

    private static async Task RunChatAsync(IChatCompletion chatCompletion)
    {
        var chatHistory = chatCompletion.CreateNewChat("You are a librarian, expert about books");

        // First user message
        chatHistory.AddUserMessage("Hi, I'm looking for book 3 different book suggestions about sci-fi");
        await MessageOutputAsync(chatHistory);

        var chatRequestSettings = new ChatRequestSettings
        {
            MaxTokens = 1024,
            ResultsPerPrompt = 2,
            Temperature = 1,
            TopP = 0.5,
            FrequencyPenalty = 0,
        };

        // First bot assistant message
        foreach (IChatResult chatCompletionResult in await chatCompletion.GetChatCompletionsAsync(chatHistory, chatRequestSettings))
        {
            ChatMessageBase chatMessage = await chatCompletionResult.GetChatMessageAsync();
            chatHistory.Add(chatMessage);
            await MessageOutputAsync(chatHistory);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Outputs the last message of the chat history
    /// </summary>
    private static Task MessageOutputAsync(ChatHistory chatHistory)
    {
        var message = chatHistory.Messages.Last();

        Console.WriteLine($"{message.Role}: {message.Content}");
        Console.WriteLine("------------------------");

        return Task.CompletedTask;
    }
}
