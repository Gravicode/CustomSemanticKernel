﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.CoreSkills;

/// <summary>
/// TimeSkill provides a set of functions to get the current time and date.
/// </summary>
/// <example>
/// Usage: kernel.ImportSkill("time", new TimeSkill());
/// Examples:
/// {{time.date}}            => Sunday, 12 January, 2031
/// {{time.today}}           => Sunday, 12 January, 2031
/// {{time.now}}             => Sunday, January 12, 2031 9:15 PM
/// {{time.utcNow}}          => Sunday, January 13, 2031 5:15 AM
/// {{time.time}}            => 09:15:07 PM
/// {{time.year}}            => 2031
/// {{time.month}}           => January
/// {{time.monthNumber}}     => 01
/// {{time.day}}             => 12
/// {{time.dayOfMonth}}      => 12
/// {{time.dayOfWeek}}       => Sunday
/// {{time.hour}}            => 9 PM
/// {{time.hourNumber}}      => 21
/// {{time.daysAgo $days}}   => Sunday, January 12, 2025 9:15 PM
/// {{time.lastMatchingDay $dayName}} => Sunday, 7 May, 2023
/// {{time.minute}}          => 15
/// {{time.minutes}}         => 15
/// {{time.second}}          => 7
/// {{time.seconds}}         => 7
/// {{time.timeZoneOffset}}  => -08:00
/// {{time.timeZoneName}}    => PST
/// </example>
/// <remark>
/// Note: the time represents the time on the hw/vm/machine where the kernel is running.
/// TODO: import and use user's timezone
/// </remark>
public sealed class TimeSkill
{
    /// <summary>
    /// Get the current date
    /// </summary>
    /// <example>
    /// {{time.date}} => Sunday, 12 January, 2031
    /// </example>
    /// <returns> The current date </returns>
    [SKFunction, Description("Get the current date")]
    public string Date() =>
        // Example: Sunday, 12 January, 2025
        DateTimeOffset.Now.ToString("D", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current date
    /// </summary>
    /// <example>
    /// {{time.today}} => Sunday, 12 January, 2031
    /// </example>
    /// <returns> The current date </returns>
    [SKFunction, Description("Get the current date")]
    public string Today() => this.Date();

    /// <summary>
    /// Get the current date and time in the local time zone"
    /// </summary>
    /// <example>
    /// {{time.now}} => Sunday, January 12, 2025 9:15 PM
    /// </example>
    /// <returns> The current date and time in the local time zone </returns>
    [SKFunction, Description("Get the current date and time in the local time zone")]
    public string Now() =>
        // Sunday, January 12, 2025 9:15 PM
        DateTimeOffset.Now.ToString("f", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current UTC date and time
    /// </summary>
    /// <example>
    /// {{time.utcNow}} => Sunday, January 13, 2025 5:15 AM
    /// </example>
    /// <returns> The current UTC date and time </returns>
    [SKFunction, Description("Get the current UTC date and time")]
    public string UtcNow() =>
        // Sunday, January 13, 2025 5:15 AM
        DateTimeOffset.UtcNow.ToString("f", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current time
    /// </summary>
    /// <example>
    /// {{time.time}} => 09:15:07 PM
    /// </example>
    /// <returns> The current time </returns>
    [SKFunction, Description("Get the current time")]
    public string Time() =>
        // Example: 09:15:07 PM
        DateTimeOffset.Now.ToString("hh:mm:ss tt", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current year
    /// </summary>
    /// <example>
    /// {{time.year}} => 2025
    /// </example>
    /// <returns> The current year </returns>
    [SKFunction, Description("Get the current year")]
    public string Year() =>
        // Example: 2025
        DateTimeOffset.Now.ToString("yyyy", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current month name
    /// </summary>
    /// <example>
    /// {time.month}} => January
    /// </example>
    /// <returns> The current month name </returns>
    [SKFunction, Description("Get the current month name")]
    public string Month() =>
        // Example: January
        DateTimeOffset.Now.ToString("MMMM", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current month number
    /// </summary>
    /// <example>
    /// {{time.monthNumber}} => 01
    /// </example>
    /// <returns> The current month number </returns>
    [SKFunction, Description("Get the current month number")]
    public string MonthNumber() =>
        // Example: 01
        DateTimeOffset.Now.ToString("MM", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current day of the month
    /// </summary>
    /// <example>
    /// {{time.day}} => 12
    /// </example>
    /// <returns> The current day of the month </returns>
    [SKFunction, Description("Get the current day of the month")]
    public string Day() =>
        // Example: 12
        DateTimeOffset.Now.ToString("dd", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the date a provided number of days in the past
    /// </summary>
    /// <example>
    /// SKContext["input"] = "3"
    /// {{time.daysAgo}} => Sunday, January 12, 2025 9:15 PM
    /// </example>
    /// <returns> The date the provided number of days before today </returns>
    [SKFunction]
    [Description("Get the date offset by a provided number of days from today")]
    public string DaysAgo([Description("The number of days to offset from today"), SKName("input")] double daysOffset) =>
        DateTimeOffset.Now.AddDays(-daysOffset).ToString("D", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current day of the week
    /// </summary>
    /// <example>
    /// {{time.dayOfWeek}} => Sunday
    /// </example>
    /// <returns> The current day of the week </returns>
    [SKFunction, Description("Get the current day of the week")]
    public string DayOfWeek() =>
        // Example: Sunday
        DateTimeOffset.Now.ToString("dddd", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current clock hour
    /// </summary>
    /// <example>
    /// {{time.hour}} => 9 PM
    /// </example>
    /// <returns> The current clock hour </returns>
    [SKFunction, Description("Get the current clock hour")]
    public string Hour() =>
        // Example: 9 PM
        DateTimeOffset.Now.ToString("h tt", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the current clock 24-hour number
    /// </summary>
    /// <example>
    /// {{time.hourNumber}} => 21
    /// </example>
    /// <returns> The current clock 24-hour number </returns>
    [SKFunction, Description("Get the current clock 24-hour number")]
    public string HourNumber() =>
        // Example: 21
        DateTimeOffset.Now.ToString("HH", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the date of the previous day matching the supplied day name
    /// </summary>
    /// <example>
    /// {{time.lastMatchingDay $dayName}} => Sunday, 7 May, 2023
    /// </example>
    /// <returns> The date of the last instance of this day name </returns>
    /// <exception cref="ArgumentOutOfRangeException">dayName is not a recognized name of a day of the week</exception>
    [SKFunction]
    [Description("Get the date of the last day matching the supplied week day name in English. Example: Che giorno era 'Martedi' scorso -> dateMatchingLastDayName 'Tuesday' => Tuesday, 16 May, 2023")]
    public string DateMatchingLastDayName([Description("The day name to match"), SKName("input")] DayOfWeek dayName)
    {
        DateTimeOffset dateTime = DateTimeOffset.Now;

        // Walk backwards from the previous day for up to a week to find the matching day
        for (int i = 1; i <= 7; ++i)
        {
            dateTime = dateTime.AddDays(-1);
            if (dateTime.DayOfWeek == dayName)
            {
                break;
            }
        }

        return dateTime.ToString("D", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Get the minutes on the current hour
    /// </summary>
    /// <example>
    /// {{time.minute}} => 15
    /// </example>
    /// <returns> The minutes on the current hour </returns>
    [SKFunction, Description("Get the minutes on the current hour")]
    public string Minute() =>
        // Example: 15
        DateTimeOffset.Now.ToString("mm", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the seconds on the current minute
    /// </summary>
    /// <example>
    /// {{time.second}} => 7
    /// </example>
    /// <returns> The seconds on the current minute </returns>
    [SKFunction, Description("Get the seconds on the current minute")]
    public string Second() =>
        // Example: 07
        DateTimeOffset.Now.ToString("ss", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the local time zone offset from UTC
    /// </summary>
    /// <example>
    /// {{time.timeZoneOffset}} => -08:00
    /// </example>
    /// <returns> The local time zone offset from UTC </returns>
    [SKFunction, Description("Get the local time zone offset from UTC")]
    public string TimeZoneOffset() =>
        // Example: -08:00
        DateTimeOffset.Now.ToString("%K", CultureInfo.CurrentCulture);

    /// <summary>
    /// Get the local time zone name
    /// </summary>
    /// <example>
    /// {{time.timeZoneName}} => PST
    /// </example>
    /// <remark>
    /// Note: this is the "current" timezone and it can change over the year, e.g. from PST to PDT
    /// </remark>
    /// <returns> The local time zone name </returns>
    [SKFunction, Description("Get the local time zone name")]
    public string TimeZoneName() =>
        // Example: PST
        // Note: this is the "current" timezone and it can change over the year, e.g. from PST to PDT
        TimeZoneInfo.Local.DisplayName;
}
