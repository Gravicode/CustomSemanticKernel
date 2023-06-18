﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant.Diagnostics;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant.Http;
using Microsoft.SemanticKernel.Connectors.Memory.Qdrant.Http.ApiSchema;

namespace Microsoft.SemanticKernel.Connectors.Memory.Qdrant;

/// <summary>
/// An implementation of a client for the Qdrant Vector Database. This class is used to
/// connect, create, delete, and get embeddings data from a Qdrant Vector Database instance.
/// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable. Explanation - In this case, there is no need to dispose because either the NonDisposableHttpClientHandler or a custom HTTP client is being used.
public sealed class QdrantVectorDbClient : IQdrantVectorDbClient
#pragma warning restore CA1001 // Types that own disposable fields should be disposable.  Explanation - In this case, there is no need to dispose because either the NonDisposableHttpClientHandler or a custom HTTP client is being used.
{
    /// <summary>
    /// The endpoint for the Qdrant service.
    /// </summary>
    [Obsolete("This property is deprecated and will be removed in one of the next SK SDK versions.")]
    public string BaseAddress => this._httpClient.BaseAddress.ToString();

    /// <summary>
    /// The port for the Qdrant service.
    /// </summary>
    [Obsolete("This property is deprecated and will be removed in one of the next SK SDK versions.")]
    public int Port => this._httpClient.BaseAddress.Port;

    /// <summary>
    /// The constructor for the QdrantVectorDbClient.
    /// </summary>
    /// <param name="endpoint"></param>
    /// <param name="vectorSize"></param>
    /// <param name="port"></param>
    /// <param name="httpClient"></param>
    /// <param name="log"></param>
    [Obsolete("This constructor is deprecated and will be removed in one of the next SK SDK versions. Please use one of the alternative constructors.")]
    public QdrantVectorDbClient(
        string endpoint,
        int vectorSize,
        int? port = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
    {
        Verify.ArgNotNullOrEmpty(endpoint, "Qdrant endpoint cannot be null or empty");

        this._vectorSize = vectorSize;
        this._logger = log ?? NullLogger<QdrantVectorDbClient>.Instance;
        this._httpClient = httpClient ?? new HttpClient(HttpHandlers.CheckCertificateRevocation);
        this._httpClient.BaseAddress = SanitizeEndpoint(endpoint, port);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorDbClient"/> class.
    /// </summary>
    /// <param name="endpoint">The Qdrant Vector Database endpoint.</param>
    /// <param name="vectorSize">The size of the vectors used in the Qdrant Vector Database.</param>
    /// <param name="logger">Optional logger instance.</param>
    public QdrantVectorDbClient(
        string endpoint,
        int vectorSize,
        ILogger? logger = null)
    {
        this._vectorSize = vectorSize;
        this._httpClient = new HttpClient(NonDisposableHttpClientHandler.Instance, disposeHandler: false);
        this._httpClient.BaseAddress = SanitizeEndpoint(endpoint);
        this._logger = logger ?? NullLogger<QdrantVectorDbClient>.Instance;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorDbClient"/> class.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> instance used for making HTTP requests.</param>
    /// <param name="vectorSize">The size of the vectors used in the Qdrant Vector Database.</param>
    /// <param name="endpoint">The optional endpoint URL for the Qdrant Vector Database. If not specified, the base address of the HTTP client is used.</param>
    /// <param name="logger">Optional logger instance.</param>
    public QdrantVectorDbClient(
        HttpClient httpClient,
        int vectorSize,
        string? endpoint = null,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(httpClient.BaseAddress?.AbsoluteUri) && string.IsNullOrEmpty(endpoint))
        {
            throw new AIException(
                AIException.ErrorCodes.InvalidConfiguration,
                "The HttpClient BaseAddress and endpoint are both null or empty. Please ensure at least one is provided.");
        }

        this._httpClient = httpClient;
        this._vectorSize = vectorSize;
        this._endpointOverride = string.IsNullOrEmpty(endpoint) ? null : SanitizeEndpoint(endpoint!);
        this._logger = logger ?? NullLogger<QdrantVectorDbClient>.Instance;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<QdrantVectorRecord> GetVectorsByIdAsync(string collectionName, IEnumerable<string> pointIds, bool withVectors = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Searching vectors by point ID");

        using HttpRequestMessage request = GetVectorsRequest.Create(collectionName)
            .WithPointIDs(pointIds)
            .WithPayloads(true)
            .WithVectors(withVectors)
            .Build();

        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            this._logger.LogDebug("Vectors not found {0}", e.Message);
            yield break;
        }

        var data = JsonSerializer.Deserialize<GetVectorsResponse>(responseContent);

        if (data == null)
        {
            this._logger.LogWarning("Unable to deserialize Get response");
            yield break;
        }

        if (!data.Result.Any())
        {
            this._logger.LogWarning("Vectors not found");
            yield break;
        }

        var records = data.Result;

#pragma warning disable CS8604 // The request specifically asked for a payload to be in the response
        foreach (var record in records)
        {
            yield return new QdrantVectorRecord(
                pointId: record.Id,
                embedding: record.Vector ?? Array.Empty<float>(),
                record.Payload,
                tags: null);
        }
#pragma warning restore CS8604
    }

    /// <inheritdoc/>
    public async Task<QdrantVectorRecord?> GetVectorByPayloadIdAsync(string collectionName, string metadataId, bool withVector = false, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = SearchVectorsRequest.Create(collectionName)
            .SimilarTo(new float[this._vectorSize])
            .HavingExternalId(metadataId)
            .IncludePayLoad()
            .TakeFirst()
            .IncludeVectorData(withVector)
            .Build();

        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            this._logger.LogDebug("Request for vector with payload ID failed {0}", e.Message);
            return null;
        }

        var data = JsonSerializer.Deserialize<SearchVectorsResponse>(responseContent);

        if (data == null)
        {
            this._logger.LogWarning("Unable to deserialize Search response");
            return null;
        }

        if (!data.Results.Any())
        {
            this._logger.LogDebug("Vector not found");
            return null;
        }

        var point = data.Results.First();

        var record = new QdrantVectorRecord(
            pointId: point.Id,
            embedding: point.Vector ?? Array.Empty<float>(),
            payload: point.Payload,
            tags: null);
        this._logger.LogDebug("Vector found}");

        return record;
    }

    /// <inheritdoc/>
    public async Task DeleteVectorsByIdAsync(string collectionName, IEnumerable<string> pointIds, CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Deleting vector by point ID");

        Verify.NotNullOrEmpty(collectionName, "Collection name is empty");
        Verify.NotNull(pointIds, "Qdrant point IDs are NULL");

        using var request = DeleteVectorsRequest.DeleteFrom(collectionName)
            .DeleteRange(pointIds)
            .Build();
        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var result = JsonSerializer.Deserialize<QdrantResponse>(responseContent);
            if (result?.Status == "ok")
            {
                this._logger.LogDebug("Vector being deleted");
            }
            else
            {
                this._logger.LogWarning("Vector delete failed");
            }
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError(e, "Vector delete request failed: {0}", e.Message);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteVectorByPayloadIdAsync(string collectionName, string metadataId, CancellationToken cancellationToken = default)
    {
        QdrantVectorRecord? existingRecord = await this.GetVectorByPayloadIdAsync(collectionName, metadataId, false, cancellationToken).ConfigureAwait(false);

        if (existingRecord == null)
        {
            this._logger.LogDebug("Vector not found, nothing to delete");
            return;
        }

        this._logger.LogDebug("Vector found, deleting");

        using var request = DeleteVectorsRequest
            .DeleteFrom(collectionName)
            .DeleteVector(existingRecord.PointId)
            .Build();

        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var result = JsonSerializer.Deserialize<QdrantResponse>(responseContent);
            if (result?.Status == "ok")
            {
                this._logger.LogDebug("Vector being deleted");
            }
            else
            {
                this._logger.LogWarning("Vector delete failed");
            }
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError(e, "Vector delete request failed: {0}", e.Message);
        }
    }

    /// <inheritdoc/>
    public async Task UpsertVectorsAsync(string collectionName, IEnumerable<QdrantVectorRecord> vectorData, CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Upserting vectors");
        Verify.NotNull(vectorData, "The vector data entries are NULL");
        Verify.NotNullOrEmpty(collectionName, "Collection name is empty");

        using var request = UpsertVectorRequest.Create(collectionName)
            .UpsertRange(vectorData)
            .Build();
        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            var result = JsonSerializer.Deserialize<UpsertVectorResponse>(responseContent);
            if (result?.Status == "ok")
            {
                this._logger.LogDebug("Vectors upserted");
            }
            else
            {
                this._logger.LogWarning("Vector upserts failed");
            }
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError(e, "Vector upserts request failed: {0}", e.Message);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<(QdrantVectorRecord, double)> FindNearestInCollectionAsync(
        string collectionName,
        IEnumerable<float> target,
        double threshold,
        int top = 1,
        bool withVectors = false,
        IEnumerable<string>? requiredTags = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Searching top {0} nearest vectors", top);

        Verify.NotNull(target, "The given vector is NULL");

        using HttpRequestMessage request = SearchVectorsRequest
            .Create(collectionName)
            .SimilarTo(target)
            .HavingTags(requiredTags)
            .WithScoreThreshold(threshold)
            .IncludePayLoad()
            .IncludeVectorData(withVectors)
            .Take(top)
            .Build();

        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            this._logger.LogWarning("No vectors were found.");
            yield break;
        }

        response.EnsureSuccessStatusCode();

        var data = JsonSerializer.Deserialize<SearchVectorsResponse>(responseContent);

        if (data == null)
        {
            this._logger.LogWarning("Unable to deserialize Search response");
            yield break;
        }

        if (!data.Results.Any())
        {
            this._logger.LogWarning("Nothing found");
            yield break;
        }

        var result = new List<(QdrantVectorRecord, double)>();

        foreach (var v in data.Results)
        {
            var record = new QdrantVectorRecord(
                pointId: v.Id,
                embedding: v.Vector ?? Array.Empty<float>(),
                payload: v.Payload);

            result.Add((record, v.Score ?? 0.0));
        }

        // Qdrant search results are currently sorted by id, alphabetically, sort list in place
        result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        foreach (var kv in result)
        {
            yield return kv;
        }
    }

    /// <inheritdoc/>
    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Creating collection {0}", collectionName);

        using var request = CreateCollectionRequest
            .Create(collectionName, this._vectorSize, QdrantDistanceType.Cosine)
            .Build();

        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        // Creation is idempotent, ignore error (and for now ignore vector size)
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            if (responseContent.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0) { return; }
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError(e, "Collection upsert failed: {0}, {1}", e.Message, responseContent);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Deleting collection {0}", collectionName);

        using var request = DeleteCollectionRequest.Create(collectionName).Build();
        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        // Deletion is idempotent, ignore error
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            this._logger.LogError(e, "Collection deletion failed: {0}, {1}", e.Message, responseContent);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DoesCollectionExistAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Fetching collection {0}", collectionName);

        using var request = GetCollectionsRequest.Create(collectionName).Build();
        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        else
        {
            this._logger.LogError("Collection fetch failed: {0}, {1}", response.StatusCode, responseContent);
            return false;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ListCollectionsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this._logger.LogDebug("Listing collections");

        using var request = ListCollectionsRequest.Create().Build();
        (HttpResponseMessage response, string responseContent) = await this.ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

        var collections = JsonSerializer.Deserialize<ListCollectionsResponse>(responseContent);

        foreach (var collection in collections?.Result?.Collections ?? Enumerable.Empty<ListCollectionsResponse.CollectionResult.CollectionDescription>())
        {
            yield return collection.Name;
        }
    }

    #region private ================================================================================

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly int _vectorSize;
    private readonly Uri? _endpointOverride = null;

    private static Uri SanitizeEndpoint(string endpoint, int? port = null)
    {
        Verify.IsValidUrl(nameof(endpoint), endpoint, false, true, false);

        UriBuilder builder = new(endpoint);
        if (port.HasValue) { builder.Port = port.Value; }

        return builder.Uri;
    }

    private async Task<(HttpResponseMessage response, string responseContent)> ExecuteHttpRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        //Apply endpoint override if it's specified.
        if (this._endpointOverride != null)
        {
            request.RequestUri = new Uri(this._endpointOverride, request.RequestUri);
        }

        HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            this._logger.LogTrace("Qdrant responded successfully");
        }
        else
        {
            this._logger.LogTrace("Qdrant responded with error");
        }

        return (response, responseContent);
    }

    #endregion
}
