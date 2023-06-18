﻿// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.AI.Embeddings;
using Microsoft.SemanticKernel.Connectors.Memory.Pinecone;
using Microsoft.SemanticKernel.Connectors.Memory.Pinecone.Model;
using Microsoft.SemanticKernel.Memory;
using Moq;
using Xunit;

namespace SemanticKernel.Connectors.UnitTests.Memory.Pinecone;

public class PineconeMemoryStoreTests
{
    private readonly string _id = "Id";
    private readonly string _id2 = "Id2";
    private readonly string _id3 = "Id3";

    private readonly string _text = "text";
    private readonly string _text2 = "text2";
    private readonly string _text3 = "text3";

    private readonly string _description = "description";
    private readonly string _description2 = "description2";
    private readonly string _description3 = "description3";

    private readonly Embedding<float> _embedding = new(new float[] { 1, 1, 1 });
    private readonly Embedding<float> _embedding2 = new(new float[] { 2, 2, 2 });
    private readonly Embedding<float> _embedding3 = new(new float[] { 3, 3, 3 });

    private readonly Mock<IPineconeClient> _mockPineconeClient;
    private readonly Mock<ILogger<PineconeMemoryStore>> _mockLogger = new();

    private readonly PineconeMemoryStore _pineconeMemoryStore;

    public PineconeMemoryStoreTests()
    {
        this._mockPineconeClient = new Mock<IPineconeClient>();
        this._pineconeMemoryStore = new PineconeMemoryStore(this._mockPineconeClient.Object, this._mockLogger.Object);
    }

    [Fact]
    public void ConnectionCanBeInitialized()
    {
        // Arrange & Act
        PineconeMemoryStore memoryStore = new(this._mockPineconeClient.Object, this._mockLogger.Object);

        // Assert
        Assert.NotNull(memoryStore);
    }

    [Fact]
    public async Task ItThrowsExceptionOnIndexCreationAsync()
    {
        // Arrange
        this._mockPineconeClient
            .Setup<Task<bool>>(x => x.DoesIndexExistAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var exception = await Assert.ThrowsAsync<PineconeMemoryException>(async () => await this._pineconeMemoryStore.CreateCollectionAsync("test"));

        // Assert
        this._mockPineconeClient
            .Verify<Task<bool>>(x => x.DoesIndexExistAsync("test", It.IsAny<CancellationToken>()), Times.Once());

        Assert.Equal(PineconeMemoryException.ErrorCodes.IndexNotReady, exception.ErrorCode);
    }

    [Fact]
    public async Task ItWillNotOverwriteExistingIndexAsync()
    {
        // Arrange
        this._mockPineconeClient
            .Setup<Task<bool>>(x => x.DoesIndexExistAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        this._mockPineconeClient
            .Setup<Task>(x => x.CreateIndexAsync(It.IsAny<IndexDefinition>(), It.IsAny<CancellationToken>()));

        // Act
        await this._pineconeMemoryStore.CreateCollectionAsync("test");

        // Assert
        this._mockPineconeClient
            .Verify<Task<bool>>(x => x.DoesIndexExistAsync("test", It.IsAny<CancellationToken>()), Times.Once());
        this._mockPineconeClient
            .Verify<Task>(x => x.CreateIndexAsync(IndexDefinition.Default("test"), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task ItListsIndexesAsync()
    {
        // Arrange
        this._mockPineconeClient
            .Setup<IAsyncEnumerable<string?>>(x => x.ListIndexesAsync(It.IsAny<CancellationToken>()))
            .Returns(new string[] { "test1", "test2" }.ToAsyncEnumerable());

        // Act
        var collections = await this._pineconeMemoryStore.GetCollectionsAsync().ToListAsync();

        // Assert
        this._mockPineconeClient.Verify<IAsyncEnumerable<string?>>(x => x.ListIndexesAsync(It.IsAny<CancellationToken>()), Times.Once());
        Assert.Equal(2, collections.Count);
        Assert.Equal("test1", collections[0]);
        Assert.Equal("test2", collections[1]);
    }

    [Fact]
    public async Task ItDeletesIndexAsync()
    {
        // Arrange
        this._mockPineconeClient
            .Setup<Task>(x => x.DeleteIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()));

        this._mockPineconeClient
            .Setup<Task<bool>>(x => x.DoesIndexExistAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await this._pineconeMemoryStore.DeleteCollectionAsync("test");

        // Assert
        this._mockPineconeClient
            .Verify<Task>(x => x.DeleteIndexAsync("test", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task UpsertAsyncInsertsNewDocumentAsync()
    {
        // Arrange
        string indexName = "test-index";
        MemoryRecord memoryRecord = MemoryRecord.LocalRecord(
            this._id,
            this._text,
            this._description,
            this._embedding);

        this._mockPineconeClient
            .Setup<IAsyncEnumerable<PineconeDocument?>>(x =>
                x.FetchVectorsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<PineconeDocument?>());

        this._mockPineconeClient
            .Setup<Task<int>>(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PineconeDocument>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await this._pineconeMemoryStore.UpsertAsync(indexName, memoryRecord);

        // Assert
        this._mockPineconeClient.Verify<Task<int>>(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PineconeDocument>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertBatchAsyncProcessesMultipleDocumentsAsync()
    {
        // Arrange
        string indexName = "test-index";

        MemoryRecord memoryRecord = MemoryRecord.LocalRecord(
            this._id,
            this._text,
            this._description,
            this._embedding);
        MemoryRecord memoryRecord2 = MemoryRecord.LocalRecord(
            this._id2,
            this._text2,
            this._description2,
            this._embedding2);
        MemoryRecord memoryRecord3 = MemoryRecord.LocalRecord(
            this._id3,
            this._text3,
            this._description3,
            this._embedding3);

        List<MemoryRecord> records = new()
            { memoryRecord, memoryRecord2, memoryRecord3 };

        this._mockPineconeClient
            .Setup<IAsyncEnumerable<PineconeDocument?>>(x =>
                x.FetchVectorsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<PineconeDocument?>());

        // Act
        List<string> upsertBatch = await this._pineconeMemoryStore.UpsertBatchAsync(indexName, records).ToListAsync();

        // Assert
        this._mockPineconeClient.Verify(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<IEnumerable<PineconeDocument>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(3, upsertBatch.Count);

        Assert.Equal(memoryRecord.Metadata.Id, upsertBatch[0]);
        Assert.Equal(memoryRecord2.Metadata.Id, upsertBatch[1]);
        Assert.Equal(memoryRecord3.Metadata.Id, upsertBatch[2]);
    }

    [Fact]
    public async Task TestRemoveAsync()
    {
        // Arrange
        string collectionName = "testCollection";
        string key = "doc1";

        this._mockPineconeClient
            .Setup<Task>(x => x.DeleteAsync(collectionName, new[] { key }, "", null, false, CancellationToken.None))
            .Returns(Task.CompletedTask);

        // Act
        await this._pineconeMemoryStore.RemoveAsync(collectionName, key);

        // Assert
        this._mockPineconeClient.Verify(x => x.DeleteAsync(collectionName, new[] { key }, "", null, false, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task TestGetNearestMatchesAsync()
    {
        // Arrange
        Embedding<float> embedding = new(new float[] { 0.1f, 0.2f });

        List<(PineconeDocument, double)> queryResults = new()
        {
            new(new()
            {
                Id = this._id,
                Metadata = new Dictionary<string, object>
                {
                    { "document_Id", "value1" },
                },
                Values = this._embedding.Vector
            }, 0.9),
            new(new()
            {
                Id = this._id2,
                Metadata = new Dictionary<string, object> { { "document_Id", "value2" } },
                Values = this._embedding2.Vector,
            }, 0.5)
        };

        this._mockPineconeClient
            .Setup<IAsyncEnumerable<(PineconeDocument, double)>>(x => x.GetMostRelevantAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<float>>(),
                It.IsAny<double>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns(queryResults.ToAsyncEnumerable());

        // Act
        List<(MemoryRecord, double)> results = await this._pineconeMemoryStore.GetNearestMatchesAsync(
            "indexName",
            new Embedding<float>(new[] { 0.1f, 0.2f, 0.3f }),
            2,
            0.5,
            true).ToListAsync();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Id", results[0].Item1.Key);
        Assert.Equal(0.9, results[0].Item2);
    }
}
