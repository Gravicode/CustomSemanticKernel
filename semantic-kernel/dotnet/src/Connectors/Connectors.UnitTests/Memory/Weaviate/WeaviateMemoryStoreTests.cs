﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Connectors.Memory.Weaviate;
using Xunit;

namespace SemanticKernel.Connectors.UnitTests.Memory.Weaviate;

/// <summary>
/// Unit tests for <see cref="WeaviateMemoryStore"/> class.
/// </summary>
public sealed class WeaviateMemoryStoreTests : IDisposable
{
    private HttpMessageHandlerStub messageHandlerStub;
    private HttpClient httpClient;

    public WeaviateMemoryStoreTests()
    {
        this.messageHandlerStub = new HttpMessageHandlerStub();

        var getResponse = new
        {
            Properties = new Dictionary<string, string> {
                { "sk_id", "fake_id" },
                { "sk_description", "fake_description" },
                { "sk_text", "fake_text" },
                { "sk_additional_metadata", "fake_additional_metadata" }
            }
        };

        this.messageHandlerStub.ResponseToReturn.Content = new StringContent(JsonSerializer.Serialize(getResponse, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, MediaTypeNames.Application.Json);

        this.httpClient = new HttpClient(this.messageHandlerStub, false);
    }

    [Fact]
    public async Task NoAuthorizationHeaderShouldBeAddedIfApiKeyIsNotProvidedAsync()
    {
        //Arrange
        using var sut = new WeaviateMemoryStore(this.httpClient, null, "https://fake-random-test-host/fake-path");

        //Act
        await sut.GetAsync("fake-collection", "fake-key");

        //Assert
        Assert.False(this.messageHandlerStub.RequestHeaders?.Contains("Authorization"));
    }

    [Fact]
    public async Task AuthorizationHeaderShouldBeAddedIfApiKeyIsProvidedAsync()
    {
        //Arrange
        using var sut = new WeaviateMemoryStore(this.httpClient, "fake-api-key", "https://fake-random-test-host/fake-path");

        //Act
        await sut.GetAsync("fake-collection", "fake-key");

        //Assert
        Assert.True(this.messageHandlerStub.RequestHeaders?.Contains("Authorization"));

        var values = this.messageHandlerStub.RequestHeaders!.GetValues("Authorization");

        var value = values.SingleOrDefault();
        Assert.Equal("fake-api-key", value);
    }

    [Fact]
    public async Task ProvidedEndpointShouldBeUsedAsync()
    {
        //Arrange
        using var sut = new WeaviateMemoryStore(this.httpClient, "fake-api-key", "https://fake-random-test-host/fake-path/");

        //Act
        await sut.GetAsync("fake-collection", "fake-key");

        //Assert
        Assert.StartsWith("https://fake-random-test-host/fake-path", this.messageHandlerStub.RequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpClientBaseAddressShouldBeUsedAsync()
    {
        //Arrange
        this.httpClient.BaseAddress = new Uri("https://fake-random-test-host/fake-path/");

        using var sut = new WeaviateMemoryStore(this.httpClient, "fake-api-key");

        //Act
        await sut.GetAsync("fake-collection", "fake-key");

        //Assert
        Assert.StartsWith("https://fake-random-test-host/fake-path", this.messageHandlerStub.RequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
        this.messageHandlerStub.Dispose();
    }
}
