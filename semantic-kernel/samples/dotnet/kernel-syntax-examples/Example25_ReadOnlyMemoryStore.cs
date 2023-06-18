﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.AI.Embeddings.VectorOperations;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Memory.Collections;

#pragma warning disable CA2201 // System.Exception is not sufficiently specific - this is a sample
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable CA1851 // Possible multiple enumerations of 'IEnumerable' collection

// ReSharper disable once InconsistentNaming
/// <summary>
/// This sample provides a custom implementation of <see cref="IMemoryStore"/> that is read only.
///     In this sample, the data is stored in a JSON string and deserialized into an
///     <see cref="IEnumerable{MemoryRecord}"/>. For this specific sample, the implementation
///     of <see cref="IMemoryStore"/> has a single collection, and thus does not need to be named.
///     It also assumes that the JSON formatted data can be deserialized into <see cref="MemoryRecord"/> objects.
/// </summary>
public static class Example25_ReadOnlyMemoryStore
{
    public static async Task RunAsync()
    {
        var store = new ReadOnlyMemoryStore(s_jsonVectorEntries);

        var embedding = new Embedding<float>(new float[] { 22, 4, 6 });

        Console.WriteLine("Reading data from custom read-only memory store");
        var memoryRecord = await store.GetAsync("collection", "key3");
        if (memoryRecord != null)
        {
            Console.WriteLine("ID = {0}, Embedding = {1}", memoryRecord.Metadata.Id, string.Join(", ", memoryRecord.Embedding.Vector));
        }

        Console.WriteLine("Getting most similar vector to {0}", string.Join(", ", embedding.Vector));
        var result = await store.GetNearestMatchAsync("collection", embedding, 0.0);
        if (result.HasValue)
        {
            Console.WriteLine("Embedding = {0}, Similarity = {1}", string.Join(", ", result.Value.Item1.Embedding.Vector), result.Value.Item2);
        }
    }

    private sealed class ReadOnlyMemoryStore : IMemoryStore
    {
        private readonly MemoryRecord[]? _memoryRecords = null;
        private readonly int _vectorSize = 3;

        public ReadOnlyMemoryStore(string valueString)
        {
            s_jsonVectorEntries = s_jsonVectorEntries.Replace("\n", string.Empty, StringComparison.Ordinal);
            s_jsonVectorEntries = s_jsonVectorEntries.Replace(" ", string.Empty, StringComparison.Ordinal);
            this._memoryRecords = JsonSerializer.Deserialize<MemoryRecord[]>(valueString);

            if (this._memoryRecords == null)
            {
                throw new Exception("Unable to deserialize memory records");
            }
        }

        public Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<MemoryRecord?> GetAsync(string collectionName, string key, bool withEmbedding = false, CancellationToken cancellationToken = default)
        {
            // Note: with this simple implementation, the MemoryRecord will always contain the embedding.
            return Task.FromResult(this._memoryRecords?.FirstOrDefault(x => x.Key == key));
        }

        public async IAsyncEnumerable<MemoryRecord> GetBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Note: with this simple implementation, the MemoryRecord will always contain the embedding.
            if (this._memoryRecords is not null)
            {
                foreach (var memoryRecord in this._memoryRecords)
                {
                    if (keys.Contains(memoryRecord.Key))
                    {
                        yield return memoryRecord;
                    }
                }
            }
        }

        public IAsyncEnumerable<string> GetCollectionsAsync(CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public async Task<(MemoryRecord, double)?> GetNearestMatchAsync(string collectionName, Embedding<float> embedding, double minRelevanceScore = 0,
            bool withEmbedding = false, CancellationToken cancellationToken = default)
        {
            // Note: with this simple implementation, the MemoryRecord will always contain the embedding.
            await foreach (var item in this.GetNearestMatchesAsync(
                collectionName: collectionName,
                embedding: embedding,
                limit: 1,
                minRelevanceScore: minRelevanceScore,
                withEmbeddings: withEmbedding,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                return item;
            }

            return default;
        }

        public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(string collectionName, Embedding<float> embedding, int limit,
            double minRelevanceScore = 0, bool withEmbeddings = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Note: with this simple implementation, the MemoryRecord will always contain the embedding.
            if (this._memoryRecords == null || this._memoryRecords.Length == 0)
            {
                yield break;
            }

            if (embedding.Count != this._vectorSize)
            {
                throw new Exception($"Embedding vector size {embedding.Count} does not match expected size of {this._vectorSize}");
            }

            TopNCollection<MemoryRecord> embeddings = new(limit);

            foreach (var item in this._memoryRecords)
            {
                double similarity = embedding.AsReadOnlySpan().CosineSimilarity(item.Embedding.AsReadOnlySpan());
                if (similarity >= minRelevanceScore)
                {
                    embeddings.Add(new(item, similarity));
                }
            }

            embeddings.SortByScore();

            foreach (var item in embeddings)
            {
                yield return (item.Value, item.Score.Value);
            }
        }

        public Task RemoveAsync(string collectionName, string key, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task RemoveBatchAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task<string> UpsertAsync(string collectionName, MemoryRecord record, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public IAsyncEnumerable<string> UpsertBatchAsync(string collectionName, IEnumerable<MemoryRecord> records, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }

    private static string s_jsonVectorEntries = @"[
        {
            ""embedding"": {
                ""vector"": [0, 0, 0 ]
            },
            ""metadata"": {
                ""is_reference"": false,
                ""external_source_name"": ""externalSourceName"",
                ""id"": ""Id1"",
                ""description"": ""description"",
                ""text"": ""text"",
                ""additional_metadata"" : ""value:""
            },
            ""key"": ""key1"",
            ""timestamp"": null
         },
         {
            ""embedding"": {
                ""vector"": [0, 0, 10 ]
            },
            ""metadata"": {
                ""is_reference"": false,
                ""external_source_name"": ""externalSourceName"",
                ""id"": ""Id2"",
                ""description"": ""description"",
                ""text"": ""text"",
                ""additional_metadata"" : ""value:""
            },
            ""key"": ""key2"",
            ""timestamp"": null
         },
         {
            ""embedding"": {
                ""vector"": [1, 2, 3 ]
            },
            ""metadata"": {
                ""is_reference"": false,
                ""external_source_name"": ""externalSourceName"",
                ""id"": ""Id3"",
                ""description"": ""description"",
                ""text"": ""text"",
                ""additional_metadata"" : ""value:""
            },
            ""key"": ""key3"",
            ""timestamp"": null
         },
         {
            ""embedding"": {
                ""vector"": [-1, -2, -3 ]
            },
            ""metadata"": {
                ""is_reference"": false,
                ""external_source_name"": ""externalSourceName"",
                ""id"": ""Id4"",
                ""description"": ""description"",
                ""text"": ""text"",
                ""additional_metadata"" : ""value:""
            },
            ""key"": ""key4"",
            ""timestamp"": null
         },
         {
            ""embedding"": {
                ""vector"": [12, 8, 4 ]
            },
            ""metadata"": {
                ""is_reference"": false,
                ""external_source_name"": ""externalSourceName"",
                ""id"": ""Id5"",
                ""description"": ""description"",
                ""text"": ""text"",
                ""additional_metadata"" : ""value:""
            },
            ""key"": ""key5"",
            ""timestamp"": null
        }
    ]";
}
