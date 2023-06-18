﻿// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.CoreSkills;

/// <summary>
/// TextMemorySkill provides a skill to save or recall information from the long or short term memory.
/// </summary>
/// <example>
/// Usage: kernel.ImportSkill("memory", new TextMemorySkill());
/// Examples:
/// SKContext["input"] = "what is the capital of France?"
/// {{memory.recall $input }} => "Paris"
/// </example>
public sealed class TextMemorySkill
{
    /// <summary>
    /// Name of the context variable used to specify which memory collection to use.
    /// </summary>
    public const string CollectionParam = "collection";

    /// <summary>
    /// Name of the context variable used to specify memory search relevance score.
    /// </summary>
    public const string RelevanceParam = "relevance";

    /// <summary>
    /// Name of the context variable used to specify a unique key associated with stored information.
    /// </summary>
    public const string KeyParam = "key";

    /// <summary>
    /// Name of the context variable used to specify the number of memories to recall
    /// </summary>
    public const string LimitParam = "limit";

    private const string DefaultCollection = "generic";
    private const double DefaultRelevance = 0.0;
    private const int DefaultLimit = 1;

    /// <summary>
    /// Creates a new instance of the TextMemorySkill
    /// </summary>
    public TextMemorySkill()
    {
    }

    /// <summary>
    /// Key-based lookup for a specific memory
    /// </summary>
    /// <param name="collection">Memories collection associated with the memory to retrieve</param>
    /// <param name="key">The key associated with the memory to retrieve.</param>
    /// <param name="context">Context containing the memory</param>
    /// <example>
    /// SKContext[TextMemorySkill.KeyParam] = "countryInfo1"
    /// {{memory.retrieve }}
    /// </example>
    [SKFunction, Description("Key-based lookup for a specific memory")]
    public async Task<string> RetrieveAsync(
        [Description("Memories collection associated with the memory to retrieve"), DefaultValue(DefaultCollection)] string? collection,
        [Description("The key associated with the memory to retrieve")] string key,
        SKContext context)
    {
        Verify.NotNullOrWhiteSpace(collection, $"{nameof(context)}.{nameof(context.Variables)}[{CollectionParam}]");
        Verify.NotNullOrWhiteSpace(key, $"{nameof(context)}.{nameof(context.Variables)}[{KeyParam}]");

        context.Log.LogTrace("Recalling memory with key '{0}' from collection '{1}'", key, collection);

        var memory = await context.Memory.GetAsync(collection, key).ConfigureAwait(false);

        return memory?.Metadata.Text ?? string.Empty;
    }

    /// <summary>
    /// Semantic search and return up to N memories related to the input text
    /// </summary>
    /// <example>
    /// SKContext["input"] = "what is the capital of France?"
    /// {{memory.recall $input }} => "Paris"
    /// </example>
    /// <param name="text">The input text to find related memories for.</param>
    /// <param name="collection">Memories collection to search.</param>
    /// <param name="relevance">The relevance score, from 0.0 to 1.0, where 1.0 means perfect match.</param>
    /// <param name="limit">The maximum number of relevant memories to recall.</param>
    /// <param name="context">Contains the memory to search.</param>
    [SKFunction, Description("Semantic search and return up to N memories related to the input text")]
    public async Task<string> RecallAsync(
        [Description("The input text to find related memories for")] string text,
        [Description("Memories collection to search"), DefaultValue(DefaultCollection)] string collection,
        [Description("The relevance score, from 0.0 to 1.0, where 1.0 means perfect match"), DefaultValue(DefaultRelevance)] double? relevance,
        [Description("The maximum number of relevant memories to recall"), DefaultValue(DefaultLimit)] int? limit,
        SKContext context)
    {
        Verify.NotNullOrWhiteSpace(collection, $"{nameof(context)}.{nameof(context.Variables)}[{CollectionParam}]");
        relevance ??= DefaultRelevance;
        limit ??= DefaultLimit;

        context.Log.LogTrace("Searching memories in collection '{0}', relevance '{1}'", collection, relevance);

        // Search memory
        List<MemoryQueryResult> memories = await context.Memory
            .SearchAsync(collection, text, limit.Value, relevance.Value, cancellationToken: context.CancellationToken)
            .ToListAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (memories.Count == 0)
        {
            context.Log.LogWarning("Memories not found in collection: {0}", collection);
            return string.Empty;
        }

        context.Log.LogTrace("Done looking for memories in collection '{0}')", collection);
        return limit == 1 ? memories[0].Metadata.Text : JsonSerializer.Serialize(memories.Select(x => x.Metadata.Text));
    }

    /// <summary>
    /// Save information to semantic memory
    /// </summary>
    /// <example>
    /// SKContext["input"] = "the capital of France is Paris"
    /// SKContext[TextMemorySkill.KeyParam] = "countryInfo1"
    /// {{memory.save $input }}
    /// </example>
    /// <param name="text">The information to save</param>
    /// <param name="collection">Memories collection associated with the information to save</param>
    /// <param name="key">The key associated with the information to save</param>
    /// <param name="context">Contains the memory to save.</param>
    [SKFunction, Description("Save information to semantic memory")]
    public async Task SaveAsync(
        [Description("The information to save")] string text,
        [Description("Memories collection associated with the information to save"), DefaultValue(DefaultCollection)] string collection,
        [Description("The key associated with the information to save")] string key,
        SKContext context)
    {
        Verify.NotNullOrWhiteSpace(collection, $"{nameof(context)}.{nameof(context.Variables)}[{CollectionParam}]");
        Verify.NotNullOrWhiteSpace(key, $"{nameof(context)}.{nameof(context.Variables)}[{KeyParam}]");

        context.Log.LogTrace("Saving memory to collection '{0}'", collection);

        await context.Memory.SaveInformationAsync(collection, text: text, id: key).ConfigureAwait(false);
    }

    /// <summary>
    /// Remove specific memory
    /// </summary>
    /// <example>
    /// SKContext[TextMemorySkill.KeyParam] = "countryInfo1"
    /// {{memory.remove }}
    /// </example>
    /// <param name="collection">Memories collection associated with the information to save</param>
    /// <param name="key">The key associated with the information to save</param>
    /// <param name="context">Contains the memory from which to remove.</param>
    [SKFunction, Description("Remove specific memory")]
    public async Task RemoveAsync(
        [Description("Memories collection associated with the information to save"), DefaultValue(DefaultCollection)] string collection,
        [Description("The key associated with the information to save")] string key,
        SKContext context)
    {
        Verify.NotNullOrWhiteSpace(collection, $"{nameof(context)}.{nameof(context.Variables)}[{CollectionParam}]");
        Verify.NotNullOrWhiteSpace(key, $"{nameof(context)}.{nameof(context.Variables)}[{KeyParam}]");

        context.Log.LogTrace("Removing memory from collection '{0}'", collection);

        await context.Memory.RemoveAsync(collection, key).ConfigureAwait(false);
    }
}
