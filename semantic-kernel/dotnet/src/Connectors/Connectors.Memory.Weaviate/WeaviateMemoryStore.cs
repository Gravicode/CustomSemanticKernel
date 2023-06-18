﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.Connectors.Memory.Weaviate.Diagnostics;
using Microsoft.SemanticKernel.Connectors.Memory.Weaviate.Http.ApiSchema;
using Microsoft.SemanticKernel.Connectors.Memory.Weaviate.Model;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Memory;

namespace Microsoft.SemanticKernel.Connectors.Memory.Weaviate;

/// <summary>
/// An implementation of <see cref="IMemoryStore" /> for Weaviate.
/// </summary>
/// <remarks>The Embedding data is saved to Weaviate instance specified in the constructor.
/// The embedding data persists between subsequent instances and has similarity search capability.
/// </remarks>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class WeaviateMemoryStore : IMemoryStore, IDisposable
{
    /// <summary>
    /// The authorization header name
    /// </summary>
    private const string AuthorizationHeaderName = nameof(HttpRequestHeader.Authorization);

    // Regex to ensure Weaviate class names confirm to the naming convention
    // https://weaviate.io/developers/weaviate/configuration/schema-configuration#class
    private static readonly Regex s_classNameRegEx = new("[^0-9a-zA-Z]+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly bool _isSelfManagedHttpClient;
    private readonly ILogger _logger;
    private bool _disposed;
    private readonly Uri? _endpoint = null;
    private string? _apiKey;

    /// <summary>
    ///     Constructor for a memory store backed by Weaviate
    /// </summary>
    [Obsolete("This constructor is deprecated and will be removed in one of the next SK SDK versions. Please use one of the alternative constructors.")]
    public WeaviateMemoryStore(string scheme, string host, int port, string? apiKey = null, HttpClient? httpClient = null, ILogger? logger = null)
    {
        Verify.NotNullOrWhiteSpace(scheme);
        Verify.NotNullOrWhiteSpace(host, "Host cannot be null or empty");

        this._logger = logger ?? NullLogger<WeaviateMemoryStore>.Instance;
        if (httpClient == null)
        {
            this._httpClient = new();
            if (!string.IsNullOrEmpty(apiKey))
            {
                this._httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderName, apiKey);
            }

            // If not passed an HttpClient, then it is the responsibility of this class
            // to ensure it is cleared up in the Dispose() method.
            this._isSelfManagedHttpClient = true;
        }
        else
        {
            this._httpClient = httpClient;
        }

        this._httpClient.BaseAddress = new($"{scheme}://{host}:{port}/v1/");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateMemoryStore"/> class.
    /// </summary>
    /// <param name="endpoint">The Weaviate server endpoint URL.</param>
    /// <param name="apiKey">The API key for accessing Weaviate server.</param>
    /// <param name="logger">Optional logger instance.</param>
    public WeaviateMemoryStore(string endpoint, string? apiKey = null, ILogger? logger = null)
    {
        Verify.NotNullOrWhiteSpace(endpoint);

        this._endpoint = new Uri(endpoint);
        this._apiKey = apiKey;
        this._logger = logger ?? NullLogger.Instance;
        this._httpClient = new HttpClient(NonDisposableHttpClientHandler.Instance, disposeHandler: false);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateMemoryStore"/> class.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> instance used for making HTTP requests.</param>
    /// <param name="apiKey">The API key for accessing Weaviate server.</param>
    /// <param name="endpoint">The optional Weaviate server endpoint URL. If not specified, the base address of the HTTP client is used.</param>
    /// <param name="logger">Optional logger instance.</param>
    public WeaviateMemoryStore(HttpClient httpClient, string? apiKey = null, string? endpoint = null, ILogger? logger = null)
    {
        Verify.NotNull(httpClient);

        if (string.IsNullOrEmpty(httpClient.BaseAddress?.AbsoluteUri) && string.IsNullOrEmpty(endpoint))
        {
            throw new AIException(
                AIException.ErrorCodes.InvalidConfiguration,
                "The HttpClient BaseAddress and endpoint are both null or empty. Please ensure at least one is provided.");
        }

        this._apiKey = apiKey;
        this._endpoint = string.IsNullOrEmpty(endpoint) ? null : new Uri(endpoint);
        this._logger = logger ?? NullLogger.Instance;
        this._httpClient = httpClient;
    }

    [Obsolete("This method is deprecated and will be removed in one of the next SK SDK versions.")]
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");

        string className = ToWeaviateFriendlyClassName(collectionName);
        string description = ToWeaviateFriendlyClassDescription(collectionName);

        this._logger.LogTrace("Creating collection: {0}, with class name: {1}", collectionName, className);

        using HttpRequestMessage request = CreateClassSchemaRequest.Create(className, description).Build();

        try
        {
            (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            CreateClassSchemaResponse? result = JsonSerializer.Deserialize<CreateClassSchemaResponse>(responseContent, s_jsonSerializerOptions);
            response.EnsureSuccessStatusCode();

            if (result == null || result.Description != description)
            {
                throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.CollectionNameConflict,
                    $"Name conflict for collection: {collectionName} with class name: {className}");
            }

            this._logger.LogTrace("Created collection: {0}, with class name: {1}", collectionName, className);
        }
        catch (HttpRequestException e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToCreateCollection,
                $"Unable to create collection: {collectionName}, with class name: {className}", e);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");

        string className = ToWeaviateFriendlyClassName(collectionName);
        this._logger.LogTrace("Does collection exist: {0}, with class name: {1}:", collectionName, className);

        using HttpRequestMessage request = GetClassRequest.Create(className).Build();

        try
        {
            (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

            // Needs to return a non-404 AND collection name should match
            bool exists = response.StatusCode != HttpStatusCode.NotFound;
            if (!exists)
            {
                this._logger.LogTrace("Collection: {0}, with class name: {1}, does not exist.", collectionName, className);
            }
            else
            {
                GetClassResponse? existing = JsonSerializer.Deserialize<GetClassResponse>(responseContent, s_jsonSerializerOptions);
                if (existing != null && existing.Description != ToWeaviateFriendlyClassDescription(collectionName))
                {
                    // ReSharper disable once CommentTypo
                    // Check that we don't have an accidental conflict.
                    // For example a collectionName of '__this_collection' and 'this_collection' are
                    // both transformed to the class name of <classNamePrefix>thiscollection - even though the external
                    // system could consider them as unique collection names.
                    throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.CollectionNameConflict, $"Unable to verify existing collection: {collectionName} with class name: {className}");
                }
            }

            return exists;
        }
        catch (Exception e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToGetClass, "Unable to get class from Weaviate", e);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GetCollectionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this._logger.LogTrace("Listing collections");

        using HttpRequestMessage request = GetSchemaRequest.Create().Build();
        string responseContent;
        try
        {
            (HttpResponseMessage response, responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToListCollections, "Unable to list collections", e);
        }

        GetSchemaResponse? getSchemaResponse = JsonSerializer.Deserialize<GetSchemaResponse>(responseContent, s_jsonSerializerOptions);
        if (getSchemaResponse == null)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToListCollections, "Unable to deserialize list collections response");
        }

        foreach (GetClassResponse? @class in getSchemaResponse.Classes!)
        {
            yield return @class.Class!;
        }
    }

    /// <inheritdoc />
    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");

        string className = ToWeaviateFriendlyClassName(collectionName);
        this._logger.LogTrace("Deleting collection: {0}, with class name: {1}", collectionName, className);

        if (await this.DoesCollectionExistAsync(collectionName, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                using HttpRequestMessage request = DeleteSchemaRequest.Create(className).Build();
                (HttpResponseMessage response, string _) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToDeleteCollection, "Collection deletion failed", e);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> UpsertAsync(string collectionName, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");

        return await this.UpsertBatchAsync(collectionName, new[] { record }, cancellationToken).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> UpsertBatchAsync(string collectionName, IEnumerable<MemoryRecord> records,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");

        this._logger.LogTrace("Upsert vectors");

        string className = ToWeaviateFriendlyClassName(collectionName);
        BatchRequest requestBuilder = BatchRequest.Create(className);
        foreach (MemoryRecord? record in records)
        {
            requestBuilder.Add(record);
        }

        using HttpRequestMessage request = requestBuilder.Build();

        string responseContent;
        try
        {
            (HttpResponseMessage response, responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToUpsertVectors, e);
        }

        BatchResponse[]? result = JsonSerializer.Deserialize<BatchResponse[]>(responseContent, s_jsonSerializerOptions);

        if (result == null)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToUpsertVectors, "Unable to deserialize batch response");
        }

        foreach (BatchResponse batchResponse in result)
        {
            yield return batchResponse.Id!;
        }
    }

    /// <inheritdoc />
    public async Task<MemoryRecord?> GetAsync(string collectionName, string key, bool withEmbedding = false, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");
        Verify.NotNullOrWhiteSpace(key, "Key is empty");

        using HttpRequestMessage request = new GetObjectRequest
        {
            Id = key,
            Additional = withEmbedding ? new[] { "vector" } : null
        }.Build();

        string responseContent;
        try
        {
            (HttpResponseMessage response, responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError("Request for vector failed {0}", e.Message);
            return null;
        }

        WeaviateObject? weaviateObject = JsonSerializer.Deserialize<WeaviateObject>(responseContent, s_jsonSerializerOptions);
        if (weaviateObject == null)
        {
            this._logger.LogError("Unable to deserialize response to WeaviateObject");
            return null;
        }

        DateTimeOffset? timestamp = weaviateObject.Properties == null
            ? null
            : weaviateObject.Properties.TryGetValue("sk_timestamp", out object value)
                ? Convert.ToDateTime(value.ToString(), CultureInfo.InvariantCulture)
                : null;

        MemoryRecord record = new(
            key: weaviateObject.Id!,
            timestamp: timestamp,
            embedding: new(weaviateObject.Vector ?? Array.Empty<float>()),
            metadata: ToMetadata(weaviateObject));

        this._logger.LogTrace("Vector found with key: {0}", key);

        return record;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MemoryRecord> GetBatchAsync(string collectionName, IEnumerable<string> keys, bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (string? key in keys)
        {
            MemoryRecord? record = await this.GetAsync(collectionName, key, withEmbeddings, cancellationToken).ConfigureAwait(false);
            if (record != null)
            {
                yield return record;
            }
            else
            {
                this._logger.LogWarning("Unable to locate object with id: {0}", key);
            }
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string collectionName, string key, CancellationToken cancellationToken = default)
    {
        Verify.NotNullOrWhiteSpace(collectionName, "Collection name is empty");
        Verify.NotNull(key, "Key is NULL");

        string className = ToWeaviateFriendlyClassName(collectionName);
        this._logger.LogTrace("Deleting vector with key: {0}, from collection {1}, with class name: {2}:", key, collectionName, className);

        DeleteObjectRequest requestBuilder = new()
        {
            Class = className,
            Id = key
        };

        using HttpRequestMessage request = requestBuilder.Build();

        try
        {
            (HttpResponseMessage response, string _) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            this._logger.LogTrace("Vector deleted");
        }
        catch (HttpRequestException e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToRemoveVectorData, "Vector delete request failed", e);
        }
    }

    /// <inheritdoc />
    public async Task RemoveBatchAsync(string collectionName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(keys.Select(async k => await this.RemoveAsync(collectionName, k, cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(MemoryRecord, double)> GetNearestMatchesAsync(
        string collectionName,
        Embedding<float> embedding,
        int limit,
        double minRelevanceScore = 0,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this._logger.LogTrace("Searching top {0} nearest vectors", limit);
        Verify.NotNull(embedding, "The given vector is NULL");
        string className = ToWeaviateFriendlyClassName(collectionName);

        using HttpRequestMessage request = new CreateGraphRequest
        {
            Class = className,
            Vector = embedding.Vector,
            Distance = minRelevanceScore,
            Limit = limit,
            WithVector = withEmbeddings
        }.Build();

        List<(MemoryRecord, double)> result = new();
        try
        {
            (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            GraphResponse? data = JsonSerializer.Deserialize<GraphResponse>(responseContent, s_jsonSerializerOptions);

            if (data == null)
            {
                this._logger.LogWarning("Unable to deserialize Search response");
                yield break;
            }

            JsonArray jsonArray = data.Data["Get"]![className]!.AsArray();

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (JsonNode? json in jsonArray)
            {
                MemoryRecord memoryRecord = DeserializeToMemoryRecord(json);
                double distance = json!["_additional"]!["distance"]!.GetValue<double>();
                result.Add((memoryRecord, distance));
            }
        }
        catch (Exception e)
        {
            throw new WeaviateMemoryException(WeaviateMemoryException.ErrorCodes.FailedToGetVectorData, "Unable to deserialize Weaviate object", e);
        }

        foreach ((MemoryRecord, double) kv in result)
        {
            yield return kv;
        }
    }

    private static MemoryRecord DeserializeToMemoryRecord(JsonNode? json)
    {
        string id = json!["_additional"]!["id"]!.GetValue<string>();
        Embedding<float> vector = Embedding<float>.Empty;
        if (json["_additional"]!["vector"] != null)
        {
            IEnumerable<float> floats = json["_additional"]!["vector"]!.AsArray().Select(a => a!.GetValue<float>());
            vector = new(floats);
        }

        string text = json["sk_text"]!.GetValue<string>();
        string description = json["sk_description"]!.GetValue<string>();
        string additionalMetadata = json["sk_additional_metadata"]!.GetValue<string>();
        string key = json["sk_id"]!.GetValue<string>();
        DateTime? timestamp = json["sk_timestamp"] != null
            ? Convert.ToDateTime(json["sk_timestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture)
            : null;

        MemoryRecord memoryRecord = MemoryRecord.LocalRecord(
            id,
            text,
            description,
            vector,
            additionalMetadata,
            key,
            timestamp);
        return memoryRecord;
    }

    /// <inheritdoc />
    public async Task<(MemoryRecord, double)?> GetNearestMatchAsync(
        string collectionName,
        Embedding<float> embedding,
        double minRelevanceScore = 0,
        bool withEmbedding = false,
        CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<(MemoryRecord, double)> results = this.GetNearestMatchesAsync(
            collectionName,
            embedding,
            minRelevanceScore: minRelevanceScore,
            limit: 1,
            withEmbeddings: withEmbedding,
            cancellationToken: cancellationToken);

        (MemoryRecord, double) record = await results.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (record.Item1, record.Item2);
    }

    // Get a class description, useful for checking name collisions
    private static string ToWeaviateFriendlyClassDescription(string collectionName)
    {
        return $"{"Semantic Kernel memory store for collection:"} {collectionName}";
    }

    // Convert a collectionName to a valid Weaviate class name
    private static string ToWeaviateFriendlyClassName(string collectionName)
    {
        // Prefix class names with to ensure proper case for Weaviate Classes
        var sanitised = s_classNameRegEx.Replace(collectionName, string.Empty);
        if (!char.IsLetter(sanitised[0]))
        {
            throw new ArgumentException("collectionName must start with a letter.", nameof(collectionName));
        }

        return !char.IsUpper(sanitised[0])
            ? string.Concat(sanitised[0].ToString().ToUpper(CultureInfo.InvariantCulture), sanitised.Substring(1))
            : sanitised;
    }

    // Execute the HTTP request
    private async Task<(HttpResponseMessage response, string responseContent)> ExecuteHttpRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancel = default)
    {
        if (this._endpoint != null)
        {
            request.RequestUri = new Uri(this._endpoint, request.RequestUri);
        }

        if (!string.IsNullOrEmpty(this._apiKey))
        {
            request.Headers.Add(AuthorizationHeaderName, this._apiKey);
        }

        HttpResponseMessage response = await this._httpClient.SendAsync(request, cancel).ConfigureAwait(false);
        string? responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        this._logger.LogTrace("Weaviate responded with {0}", response.StatusCode);
        return (response, responseContent);
    }

    private static MemoryRecordMetadata ToMetadata(WeaviateObject weaviateObject)
    {
        if (weaviateObject.Properties == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException(nameof(weaviateObject.Properties));
#pragma warning restore CA2208
        }

        return new(
            false,
            string.Empty,
            weaviateObject.Properties["sk_id"].ToString(),
            weaviateObject.Properties["sk_description"].ToString(),
            weaviateObject.Properties["sk_text"].ToString(),
            weaviateObject.Properties["sk_additional_metadata"].ToString()
        );
    }

    [Obsolete("This method is deprecated and will be removed in one of the next SK SDK versions.")]
    protected virtual void Dispose(bool disposing)
    {
        if (this._disposed)
        {
            return;
        }

        if (disposing)
        {
            // Clean-up the HttpClient if we created it.
            if (this._isSelfManagedHttpClient)
            {
                this._httpClient.Dispose();
            }
        }

        this._disposed = true;
    }
}
