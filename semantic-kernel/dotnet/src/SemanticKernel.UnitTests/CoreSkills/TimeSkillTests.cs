﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.CoreSkills;
using Xunit;

namespace SemanticKernel.UnitTests.CoreSkills;

// TODO: allow clock injection and test all functions
public class TimeSkillTests
{
    [Fact]
    public void ItCanBeInstantiated()
    {
        // Act - Assert no exception occurs
        var _ = new TimeSkill();
    }

    [Fact]
    public void ItCanBeImported()
    {
        // Arrange
        var kernel = Kernel.Builder.Build();

        // Act - Assert no exception occurs e.g. due to reflection
        kernel.ImportSkill(new TimeSkill(), "time");
    }

    [Fact]
    public void DaysAgo()
    {
        double interval = 2;
        DateTime expected = DateTime.Now.AddDays(-interval);
        var skill = new TimeSkill();
        string result = skill.DaysAgo(interval);
        DateTime returned = DateTime.Parse(result, CultureInfo.CurrentCulture);
        Assert.Equal(expected.Day, returned.Day);
        Assert.Equal(expected.Month, returned.Month);
        Assert.Equal(expected.Year, returned.Year);
    }

    [Fact]
    public void Day()
    {
        string expected = DateTime.Now.ToString("dd", CultureInfo.CurrentCulture);
        var skill = new TimeSkill();
        string result = skill.Day();
        Assert.Equal(expected, result);
        Assert.True(int.TryParse(result, out _));
    }

    [Fact]
    public async Task LastMatchingDayBadInput()
    {
        var skill = new TimeSkill();
        var context = await FunctionHelpers.CallViaKernel(skill, "DateMatchingLastDayName", ("input", "not a day name"));
        AssertExtensions.AssertIsArgumentOutOfRange(context.LastException, "input", "not a day name");
    }

    [Theory]
    [MemberData(nameof(DayOfWeekEnumerator))]
    public void LastMatchingDay(DayOfWeek dayName)
    {
        int steps = 0;
        DateTime date = DateTime.Now.Date.AddDays(-1);
        while (date.DayOfWeek != dayName && steps <= 7)
        {
            date = date.AddDays(-1);
            steps++;
        }
        bool found = date.DayOfWeek == dayName;
        Assert.True(found);

        var skill = new TimeSkill();
        string result = skill.DateMatchingLastDayName(dayName);
        DateTime returned = DateTime.Parse(result, CultureInfo.CurrentCulture);
        Assert.Equal(date.Day, returned.Day);
        Assert.Equal(date.Month, returned.Month);
        Assert.Equal(date.Year, returned.Year);
    }

    public static IEnumerable<object[]> DayOfWeekEnumerator()
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            yield return new object[] { day };
        }
    }
}
