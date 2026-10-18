// <copyright file="EcmaRegexLimits.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// This file is the limits and the count of steps of the Corvus.JsonSchema .NET translator (src/Corvus.Text.Json.CodeGeneration/EcmaRegexParser.cs),
// without what only the translator read: the order a class wrote its members in, the General_Category values .NET has
// a class for, and the measurements of the tree.

using System;
using System.Collections.Generic;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The limits on the size of one pattern, which bound the time and the memory it takes to read it and to compile
/// it, whatever the pattern. A pattern may come from outside, from a source that is not trusted. A pattern over a
/// limit is refused with <see cref="RegexPatternTooLargeException"/>, which is never the error of a pattern that is
/// not valid.
/// </summary>
internal static class EcmaRegexLimits
{
    /// <summary>The most characters of a pattern: 1,048,576.</summary>
    public const int MaxLength = 1_048_576;

    /// <summary>
    /// The most steps taken to read a pattern. A step is the reading of one character or the visit
    /// of one node of the tree, and the work on the ranges of a set is counted in the same measure (two ranges
    /// sorted are a step, sixteen copied are one). The steps stand for time, which they bound whatever the machine
    /// is doing, so that the same pattern is refused or accepted everywhere.
    /// </summary>
    public const long MaxSteps = 2_000_000;
}

/// <summary>Counts the steps taken for one pattern, and refuses the pattern past <see cref="EcmaRegexLimits.MaxSteps"/>.</summary>
internal sealed class EcmaRegexWork
{
    private long steps;

    /// <summary>Initializes a new instance of the <see cref="EcmaRegexWork"/> class.</summary>
    public EcmaRegexWork()
    {
        // What the tables of Unicode did on this thread for anything else is not this pattern's.
        EcmaRegexUnicode.TakeSteps();
    }

    /// <summary>Gets or sets a value indicating whether the whole pattern has been read, and is a valid ECMA-262 pattern.</summary>
    public bool Read { get; set; }

    /// <summary>Gets the steps taken so far.</summary>
    public long Steps => this.steps;

    /// <summary>Counts steps.</summary>
    /// <param name="count">How many.</param>
    /// <exception cref="RegexPatternTooLargeException">The pattern has taken more than <see cref="EcmaRegexLimits.MaxSteps"/>.</exception>
    public void Spend(long count = 1)
    {
        // What the tables of Unicode did since the last call is counted with it.
        this.steps += count + EcmaRegexUnicode.TakeSteps();
        if (this.steps > EcmaRegexLimits.MaxSteps)
        {
            string limit = "It takes more than " + EcmaRegexLimits.MaxSteps.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " steps to read.";
            throw new RegexPatternTooLargeException(limit, limit, this.Read);
        }
    }
}
