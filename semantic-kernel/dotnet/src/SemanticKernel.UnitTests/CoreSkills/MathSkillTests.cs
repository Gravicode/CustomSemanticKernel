﻿// Copyright (c) Microsoft. All rights reserved.

using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.CoreSkills;
using Xunit;

namespace SemanticKernel.UnitTests.CoreSkills;

public class MathSkillTests
{
    [Fact]
    public void ItCanBeInstantiated()
    {
        // Act - Assert no exception occurs
        var _ = new MathSkill();
    }

    [Fact]
    public void ItCanBeImported()
    {
        // Arrange
        var kernel = Kernel.Builder.Build();

        // Act - Assert no exception occurs e.g. due to reflection
        kernel.ImportSkill(new MathSkill(), "math");
    }

    [Theory]
    [InlineData("10", "10", "20")]
    [InlineData("0", "10", "10")]
    [InlineData("0", "-10", "-10")]
    [InlineData("10", "0", "10")]
    [InlineData("-1", "10", "9")]
    [InlineData("-10", "10", "0")]
    [InlineData("-192", "13", "-179")]
    [InlineData("-192", "-13", "-205")]
    public async Task AddWhenValidParametersShouldSucceedAsync(string initialValue, string amount, string expectedResult)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Add", ("input", initialValue), ("amount", amount));

        // Assert
        Assert.Equal(expectedResult, context.Variables.Input);
    }

    [Theory]
    [InlineData("10", "10", "0")]
    [InlineData("0", "10", "-10")]
    [InlineData("10", "0", "10")]
    [InlineData("100", "-10", "110")]
    [InlineData("100", "102", "-2")]
    [InlineData("-1", "10", "-11")]
    [InlineData("-10", "10", "-20")]
    [InlineData("-192", "13", "-205")]
    public async Task SubtractWhenValidParametersShouldSucceedAsync(string initialValue, string amount, string expectedResult)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Subtract", ("input", initialValue), ("amount", amount));    // Assert

        // Assert
        Assert.Equal(expectedResult, context.Variables.Input);
    }

    [Theory]
    [InlineData("$0")]
    [InlineData("one hundred")]
    [InlineData("20..,,2,1")]
    [InlineData(".2,2.1")]
    [InlineData("0.1.0")]
    [InlineData("00-099")]
    [InlineData("¹²¹")]
    [InlineData("2²")]
    [InlineData("zero")]
    [InlineData("-100 units")]
    [InlineData("1 banana")]
    public async Task AddWhenInvalidInitialValueShouldThrowAsync(string initialValue)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Add", ("input", initialValue), ("amount", "1"));

        // Assert
        AssertExtensions.AssertIsArgumentOutOfRange(context.LastException, "value", initialValue);
    }

    [Theory]
    [InlineData("$0")]
    [InlineData("one hundred")]
    [InlineData("20..,,2,1")]
    [InlineData(".2,2.1")]
    [InlineData("0.1.0")]
    [InlineData("00-099")]
    [InlineData("¹²¹")]
    [InlineData("2²")]
    [InlineData("zero")]
    [InlineData("-100 units")]
    [InlineData("1 banana")]
    public async Task AddWhenInvalidAmountShouldThrowAsync(string amount)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Add", ("input", "1"), ("amount", amount));

        // Assert
        AssertExtensions.AssertIsArgumentOutOfRange(context.LastException, "amount", amount);
    }

    [Theory]
    [InlineData("$0")]
    [InlineData("one hundred")]
    [InlineData("20..,,2,1")]
    [InlineData(".2,2.1")]
    [InlineData("0.1.0")]
    [InlineData("00-099")]
    [InlineData("¹²¹")]
    [InlineData("2²")]
    [InlineData("zero")]
    [InlineData("-100 units")]
    [InlineData("1 banana")]
    public async Task SubtractWhenInvalidInitialValueShouldThrowAsync(string initialValue)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Subtract", ("input", initialValue), ("amount", "1"));

        // Assert
        AssertExtensions.AssertIsArgumentOutOfRange(context.LastException, "value", initialValue);
    }

    [Theory]
    [InlineData("$0")]
    [InlineData("one hundred")]
    [InlineData("20..,,2,1")]
    [InlineData(".2,2.1")]
    [InlineData("0.1.0")]
    [InlineData("00-099")]
    [InlineData("¹²¹")]
    [InlineData("2²")]
    [InlineData("zero")]
    [InlineData("-100 units")]
    [InlineData("1 banana")]
    public async Task SubtractAsyncWhenInvalidAmountShouldThrowAsync(string amount)
    {
        // Arrange
        var target = new MathSkill();

        // Act
        var context = await FunctionHelpers.CallViaKernel(target, "Subtract", ("input", "1"), ("amount", amount));

        // Assert
        AssertExtensions.AssertIsArgumentOutOfRange(context.LastException, "amount", amount);
    }
}
