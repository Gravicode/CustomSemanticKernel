﻿// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning.Sequential;
using Microsoft.SemanticKernel.SkillDefinition;
using Moq;
using SemanticKernel.Extensions.UnitTests.XunitHelpers;
using Xunit;

namespace SemanticKernel.Extensions.UnitTests.Planning.SequentialPlanner;

public class SKContextExtensionsTests
{
    [Fact]
    public async Task CanCallGetAvailableFunctionsWithNoFunctionsAsync()
    {
        // Arrange
        var variables = new ContextVariables();
        var skills = new SkillCollection();
        var logger = TestConsoleLogger.Log;
        var cancellationToken = default(CancellationToken);

        // Arrange Mock Memory and Result
        var memory = new Mock<ISemanticTextMemory>();
        var memoryQueryResult = new MemoryQueryResult(
            new MemoryRecordMetadata(
                isReference: false,
                id: "id",
                text: "text",
                description: "description",
                externalSourceName: "sourceName",
                additionalMetadata: "value"),
            relevance: 0.8,
            embedding: null);
        var asyncEnumerable = new[] { memoryQueryResult }.ToAsyncEnumerable();
        memory.Setup(x =>
                x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        // Arrange GetAvailableFunctionsAsync parameters
        var context = new SKContext(variables, memory.Object, skills.ReadOnlySkillCollection, logger, cancellationToken);
        var config = new SequentialPlannerConfig();
        var semanticQuery = "test";

        // Act
        var result = await context.GetAvailableFunctionsAsync(config, semanticQuery);

        // Assert
        Assert.NotNull(result);
        memory.Verify(
            x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CanCallGetAvailableFunctionsWithFunctionsAsync()
    {
        // Arrange
        var variables = new ContextVariables();
        var logger = TestConsoleLogger.Log;
        var cancellationToken = default(CancellationToken);

        // Arrange FunctionView
        var functionMock = new Mock<ISKFunction>();
        var functionsView = new FunctionsView();
        var functionView = new FunctionView("functionName", "skillName", "description", new List<ParameterView>(), true, false);
        var nativeFunctionView = new FunctionView("nativeFunctionName", "skillName", "description", new List<ParameterView>(), false, false);
        functionsView.AddFunction(functionView);
        functionsView.AddFunction(nativeFunctionView);

        // Arrange Mock Memory and Result
        var skills = new Mock<ISkillCollection>();
        var memoryQueryResult =
            new MemoryQueryResult(
                new MemoryRecordMetadata(
                    isReference: false,
                    id: functionView.ToFullyQualifiedName(),
                    text: "text",
                    description: "description",
                    externalSourceName: "sourceName",
                    additionalMetadata: "value"),
                relevance: 0.8,
                embedding: null);
        var asyncEnumerable = new[] { memoryQueryResult }.ToAsyncEnumerable();
        var memory = new Mock<ISemanticTextMemory>();
        memory.Setup(x =>
                x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        skills.Setup(x => x.TryGetFunction(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<ISKFunction?>.IsAny)).Returns(true);
        skills.Setup(x => x.GetFunction(It.IsAny<string>(), It.IsAny<string>())).Returns(functionMock.Object);
        skills.Setup(x => x.GetFunctionsView(It.IsAny<bool>(), It.IsAny<bool>())).Returns(functionsView);
        skills.SetupGet(x => x.ReadOnlySkillCollection).Returns(skills.Object);

        // Arrange GetAvailableFunctionsAsync parameters
        var context = new SKContext(variables, memory.Object, skills.Object, logger, cancellationToken);
        var config = new SequentialPlannerConfig();
        var semanticQuery = "test";

        // Act
        var result = (await context.GetAvailableFunctionsAsync(config, semanticQuery)).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(functionView, result[0]);

        // Arrange update IncludedFunctions
        config.IncludedFunctions.UnionWith(new List<string> { "nativeFunctionName" });

        // Act
        result = (await context.GetAvailableFunctionsAsync(config, semanticQuery)).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // IncludedFunctions should be added to the result
        Assert.Equal(functionView, result[0]);
        Assert.Equal(nativeFunctionView, result[1]);
    }

    [Fact]
    public async Task CanCallGetAvailableFunctionsWithFunctionsWithRelevancyAsync()
    {
        // Arrange
        var variables = new ContextVariables();
        var logger = TestConsoleLogger.Log;
        var cancellationToken = default(CancellationToken);

        // Arrange FunctionView
        var functionMock = new Mock<ISKFunction>();
        var functionsView = new FunctionsView();
        var functionView = new FunctionView("functionName", "skillName", "description", new List<ParameterView>(), true, false);
        var nativeFunctionView = new FunctionView("nativeFunctionName", "skillName", "description", new List<ParameterView>(), false, false);
        functionsView.AddFunction(functionView);
        functionsView.AddFunction(nativeFunctionView);

        // Arrange Mock Memory and Result
        var skills = new Mock<ISkillCollection>();
        var memoryQueryResult =
            new MemoryQueryResult(
                new MemoryRecordMetadata(
                    isReference: false,
                    id: functionView.ToFullyQualifiedName(),
                    text: "text",
                    description: "description",
                    externalSourceName: "sourceName",
                    additionalMetadata: "value"),
                relevance: 0.8,
                embedding: null);
        var asyncEnumerable = new[] { memoryQueryResult }.ToAsyncEnumerable();
        var memory = new Mock<ISemanticTextMemory>();
        memory.Setup(x =>
                x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        skills.Setup(x => x.TryGetFunction(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<ISKFunction?>.IsAny)).Returns(true);
        skills.Setup(x => x.GetFunction(It.IsAny<string>(), It.IsAny<string>())).Returns(functionMock.Object);
        skills.Setup(x => x.GetFunctionsView(It.IsAny<bool>(), It.IsAny<bool>())).Returns(functionsView);
        skills.SetupGet(x => x.ReadOnlySkillCollection).Returns(skills.Object);

        // Arrange GetAvailableFunctionsAsync parameters
        var context = new SKContext(variables, memory.Object, skills.Object, logger, cancellationToken);
        var config = new SequentialPlannerConfig { RelevancyThreshold = 0.78 };
        var semanticQuery = "test";

        // Act
        var result = (await context.GetAvailableFunctionsAsync(config, semanticQuery)).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(functionView, result[0]);

        // Arrange update IncludedFunctions
        config.IncludedFunctions.UnionWith(new List<string> { "nativeFunctionName" });

        // Act
        result = (await context.GetAvailableFunctionsAsync(config, semanticQuery)).ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // IncludedFunctions should be added to the result
        Assert.Equal(functionView, result[0]);
        Assert.Equal(nativeFunctionView, result[1]);
    }

    [Fact]
    public async Task CanCallGetAvailableFunctionsAsyncWithDefaultRelevancyAsync()
    {
        // Arrange
        var variables = new ContextVariables();
        var skills = new SkillCollection();
        var logger = TestConsoleLogger.Log;
        var cancellationToken = default(CancellationToken);

        // Arrange Mock Memory and Result
        var memory = new Mock<ISemanticTextMemory>();
        var memoryQueryResult =
            new MemoryQueryResult(
                new MemoryRecordMetadata(
                    isReference: false,
                    id: "id",
                    text: "text",
                    description: "description",
                    externalSourceName: "sourceName",
                    additionalMetadata: "value"),
                relevance: 0.8,
                embedding: null);
        var asyncEnumerable = new[] { memoryQueryResult }.ToAsyncEnumerable();
        memory.Setup(x =>
                x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(asyncEnumerable);

        // Arrange GetAvailableFunctionsAsync parameters
        var context = new SKContext(variables, memory.Object, skills.ReadOnlySkillCollection, logger, cancellationToken);
        var config = new SequentialPlannerConfig { RelevancyThreshold = 0.78 };
        var semanticQuery = "test";

        // Act
        var result = await context.GetAvailableFunctionsAsync(config, semanticQuery);

        // Assert
        Assert.NotNull(result);
        memory.Verify(
            x => x.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
