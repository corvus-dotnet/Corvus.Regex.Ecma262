// <copyright file="GeneratedRegexAttribute.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Marks a <c>static partial</c> method or property that returns a <see cref="Regex"/> for the source generator,
/// which checks the pattern at build time (a pattern that is not valid is a build error with the ECMA-262 message)
/// and emits the member, as .NET's <c>GeneratedRegexAttribute</c> does.
/// </summary>
/// <example>
/// <code>
/// [GeneratedRegex(@"\d+", "gu")]
/// private static partial Regex Digits();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedRegexAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="GeneratedRegexAttribute"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    public GeneratedRegexAttribute(string pattern)
        : this(pattern, RegexFlags.None, -1)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeneratedRegexAttribute"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    public GeneratedRegexAttribute(string pattern, RegexFlags flags)
        : this(pattern, flags, -1)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeneratedRegexAttribute"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="matchTimeoutMilliseconds">The limit of time on a match in milliseconds, or -1 for none.</param>
    public GeneratedRegexAttribute(string pattern, RegexFlags flags, int matchTimeoutMilliseconds)
    {
        this.Pattern = pattern;
        this.Flags = flags;
        this.MatchTimeoutMilliseconds = matchTimeoutMilliseconds;
    }

    /// <summary>Initializes a new instance of the <see cref="GeneratedRegexAttribute"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags as ECMA-262 writes them: letters from <c>dgimsuvy</c>.</param>
    public GeneratedRegexAttribute(string pattern, string flags)
        : this(pattern, flags, -1)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeneratedRegexAttribute"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags as ECMA-262 writes them: letters from <c>dgimsuvy</c>.</param>
    /// <param name="matchTimeoutMilliseconds">The limit of time on a match in milliseconds, or -1 for none.</param>
    public GeneratedRegexAttribute(string pattern, string flags, int matchTimeoutMilliseconds)
    {
        this.Pattern = pattern;
        this.FlagsText = flags;
        this.Flags = Regex.ParseFlags(flags);
        this.MatchTimeoutMilliseconds = matchTimeoutMilliseconds;
    }

    /// <summary>Gets the pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets the flags.</summary>
    public RegexFlags Flags { get; }

    /// <summary>Gets the flags as given in letters, or null when they were given as <see cref="RegexFlags"/>.</summary>
    public string? FlagsText { get; }

    /// <summary>Gets the limit of time on a match in milliseconds, or -1 for none.</summary>
    public int MatchTimeoutMilliseconds { get; }
}
