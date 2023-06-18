﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Connectors.AI.HuggingFace.TextCompletion;
using Xunit;

namespace SemanticKernel.IntegrationTests.Connectors.HuggingFace.TextCompletion;

/// <summary>
/// Integration tests for <see cref="HuggingFaceTextCompletion"/>.
/// </summary>
public sealed class HuggingFaceTextCompletionTests
{
    private const string Endpoint = "http://localhost:5000/completions";
    private const string Model = "gpt2";

    private readonly IConfigurationRoot _configuration;

    public HuggingFaceTextCompletionTests()
    {
        // Load configuration
        this._configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(path: "testsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    [Fact(Skip = "This test is for manual verification.")]
    public async Task HuggingFaceLocalAndRemoteTextCompletionAsync()
    {
        // Arrange
        const string Input = "This is test";

        using var huggingFaceLocal = new HuggingFaceTextCompletion(Model, endpoint: Endpoint);
        using var huggingFaceRemote = new HuggingFaceTextCompletion(Model, apiKey: this.GetApiKey());

        // Act
        var localResponse = await huggingFaceLocal.CompleteAsync(Input, new CompleteRequestSettings());
        var remoteResponse = await huggingFaceRemote.CompleteAsync(Input, new CompleteRequestSettings());

        // Assert
        Assert.NotNull(localResponse);
        Assert.NotNull(remoteResponse);

        Assert.StartsWith(Input, localResponse, StringComparison.Ordinal);
        Assert.StartsWith(Input, remoteResponse, StringComparison.Ordinal);
    }

    [Fact(Skip = "This test is for manual verification.")]
    public async Task RemoteHuggingFaceTextCompletionWithCustomHttpClientAsync()
    {
        // Arrange
        const string Input = "This is test";

        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri("https://api-inference.huggingface.co/models");

        using var huggingFaceRemote = new HuggingFaceTextCompletion(Model, apiKey: this.GetApiKey(), httpClient: httpClient);

        // Act
        var remoteResponse = await huggingFaceRemote.CompleteAsync(Input, new CompleteRequestSettings());

        // Assert
        Assert.NotNull(remoteResponse);

        Assert.StartsWith(Input, remoteResponse, StringComparison.Ordinal);
    }

    private string GetApiKey()
    {
        return this._configuration.GetSection("HuggingFace:ApiKey").Get<string>()!;
    }
}
