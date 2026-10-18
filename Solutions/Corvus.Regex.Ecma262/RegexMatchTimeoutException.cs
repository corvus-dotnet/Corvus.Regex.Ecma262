// <copyright file="RegexMatchTimeoutException.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The error of a match that was abandoned at its limit: it ran for longer than the match timeout, or it needed
/// more backtracking memory than a match may hold. Neither is an answer.
/// </summary>
public sealed class RegexMatchTimeoutException : TimeoutException
{
    /// <summary>Initializes a new instance of the <see cref="RegexMatchTimeoutException"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="matchTimeout">The timeout.</param>
    /// <param name="memory">Whether the match was abandoned for its memory rather than its time.</param>
    public RegexMatchTimeoutException(string pattern, TimeSpan matchTimeout, bool memory)
        : base(memory
            ? "The match needed more backtracking memory than its limit."
            : "The match ran for longer than its timeout of " + matchTimeout.ToString() + ".")
    {
        this.Pattern = pattern;
        this.MatchTimeout = matchTimeout;
        this.ExceededMemory = memory;
    }

    /// <summary>Gets the pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets the timeout.</summary>
    public TimeSpan MatchTimeout { get; }

    /// <summary>Gets a value indicating whether the match was abandoned for its memory, which .NET has no exception of its own for, rather than its time.</summary>
    public bool ExceededMemory { get; }
}
