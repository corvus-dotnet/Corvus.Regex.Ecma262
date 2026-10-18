// <copyright file="RegexParseException.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The error of a pattern that is not a valid ECMA-262 pattern in the grammar of its flags.</summary>
public sealed class RegexParseException : ArgumentException
{
    /// <summary>Initializes a new instance of the <see cref="RegexParseException"/> class.</summary>
    /// <param name="reason">What is wrong, as ECMA-262 and V8 word it.</param>
    /// <param name="offset">Where it is, as an offset in the pattern in UTF-16 code units.</param>
    public RegexParseException(string reason, int offset)
        : base(reason + " at offset " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
    {
        this.Reason = reason;
        this.Offset = offset;
    }

    /// <summary>Gets what is wrong.</summary>
    public string Reason { get; }

    /// <summary>Gets where it is, as an offset in the pattern in UTF-16 code units.</summary>
    public int Offset { get; }
}
