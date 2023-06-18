﻿// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.Text;

namespace Microsoft.SemanticKernel.CoreSkills;

/// <summary>
/// <para>Semantic skill that enables conversations summarization.</para>
/// </summary>
/// <example>
/// <code>
/// var kernel Kernel.Builder.Build();
/// kernel.ImportSkill(new ConversationSummarySkill(kernel));
/// </code>
/// </example>
public class ConversationSummarySkill
{
    /// <summary>
    /// The max tokens to process in a single semantic function call.
    /// </summary>
    private const int MaxTokens = 1024;

    private readonly ISKFunction _summarizeConversationFunction;
    private readonly ISKFunction _conversationActionItemsFunction;
    private readonly ISKFunction _conversationTopicsFunction;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationSummarySkill"/> class.
    /// </summary>
    /// <param name="kernel">Kernel instance</param>
    public ConversationSummarySkill(IKernel kernel)
    {
        this._summarizeConversationFunction = kernel.CreateSemanticFunction(
            SemanticFunctionConstants.SummarizeConversationDefinition,
            skillName: nameof(ConversationSummarySkill),
            description: "Given a section of a conversation transcript, summarize the part of the conversation.",
            maxTokens: MaxTokens,
            temperature: 0.1,
            topP: 0.5);

        this._conversationActionItemsFunction = kernel.CreateSemanticFunction(
            SemanticFunctionConstants.GetConversationActionItemsDefinition,
            skillName: nameof(ConversationSummarySkill),
            description: "Given a section of a conversation transcript, identify action items.",
            maxTokens: MaxTokens,
            temperature: 0.1,
            topP: 0.5);

        this._conversationTopicsFunction = kernel.CreateSemanticFunction(
            SemanticFunctionConstants.GetConversationTopicsDefinition,
            skillName: nameof(ConversationSummarySkill),
            description: "Analyze a conversation transcript and extract key topics worth remembering.",
            maxTokens: MaxTokens,
            temperature: 0.1,
            topP: 0.5);
    }

    /// <summary>
    /// Given a long conversation transcript, summarize the conversation.
    /// </summary>
    /// <param name="input">A long conversation transcript.</param>
    /// <param name="context">The SKContext for function execution.</param>
    [SKFunction, Description("Given a long conversation transcript, summarize the conversation.")]
    public Task<SKContext> SummarizeConversationAsync(
        [Description("A long conversation transcript.")] string input,
        SKContext context)
    {
        List<string> lines = TextChunker.SplitPlainTextLines(input, MaxTokens);
        List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, MaxTokens);

        return this._summarizeConversationFunction
            .AggregatePartitionedResultsAsync(paragraphs, context);
    }

    /// <summary>
    /// Given a long conversation transcript, identify action items.
    /// </summary>
    /// <param name="input">A long conversation transcript.</param>
    /// <param name="context">The SKContext for function execution.</param>
    [SKFunction, Description("Given a long conversation transcript, identify action items.")]
    public Task<SKContext> GetConversationActionItemsAsync(
        [Description("A long conversation transcript.")] string input,
        SKContext context)
    {
        List<string> lines = TextChunker.SplitPlainTextLines(input, MaxTokens);
        List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, MaxTokens);

        return this._conversationActionItemsFunction
            .AggregatePartitionedResultsAsync(paragraphs, context);
    }

    /// <summary>
    /// Given a long conversation transcript, identify topics.
    /// </summary>
    /// <param name="input">A long conversation transcript.</param>
    /// <param name="context">The SKContext for function execution.</param>
    [SKFunction, Description("Given a long conversation transcript, identify topics worth remembering.")]
    public Task<SKContext> GetConversationTopicsAsync(
        [Description("A long conversation transcript.")] string input,
        SKContext context)
    {
        List<string> lines = TextChunker.SplitPlainTextLines(input, MaxTokens);
        List<string> paragraphs = TextChunker.SplitPlainTextParagraphs(lines, MaxTokens);

        return this._conversationTopicsFunction
            .AggregatePartitionedResultsAsync(paragraphs, context);
    }
}
