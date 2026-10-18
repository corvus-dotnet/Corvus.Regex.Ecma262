// <copyright file="RegexPatternTooLargeException.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The error of a pattern that is over one of the limits of size, which bound the time and the memory that reading
/// and compiling a pattern take whatever the pattern holds. It is not the error of a pattern that is not valid,
/// which is <see cref="RegexParseException"/>: a pattern over a limit may or may not be valid, and a caller must not
/// report it as one that is not.
/// </summary>
public sealed class RegexPatternTooLargeException : ArgumentException
{
    /// <summary>Initializes a new instance of the <see cref="RegexPatternTooLargeException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="limit">What the pattern is over, with the limit and its value, as a sentence.</param>
    /// <param name="read">Whether the whole pattern was read, and found to be a valid ECMA-262 pattern, before the limit was met.</param>
    public RegexPatternTooLargeException(string message, string limit, bool read)
        : base(message)
    {
        this.Limit = limit;
        this.Read = read;
    }

    /// <summary>Initializes a new instance of the <see cref="RegexPatternTooLargeException"/> class, for a limit met while reading.</summary>
    /// <param name="limit">What the pattern is over, with the limit and its value, as a sentence.</param>
    public RegexPatternTooLargeException(string limit)
        : this(limit, limit, read: false)
    {
    }

    /// <summary>Gets what the pattern is over, with the limit and its value, as a sentence.</summary>
    public string Limit { get; }

    /// <summary>
    /// Gets a value indicating whether the whole pattern was read, and found to be a valid ECMA-262 pattern, before
    /// the limit was met. A limit met while reading leaves the rest of the pattern unread.
    /// </summary>
    public bool Read { get; }
}
