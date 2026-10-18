// <copyright file="RegexFlags.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The flags of an ECMA-262 regular expression: those of <c>RegExp</c>, by name.</summary>
[Flags]
public enum RegexFlags
{
    /// <summary>No flags: the Annex B grammar, matched by UTF-16 code unit, as <c>new RegExp(pattern)</c> reads a pattern.</summary>
    None = 0,

    /// <summary>The <c>d</c> flag. Indices are always kept, so it changes nothing.</summary>
    HasIndices = 1,

    /// <summary>The <c>g</c> flag: <c>Regex.Replace</c> replaces every match, and without it the first.</summary>
    Global = 2,

    /// <summary>The <c>i</c> flag: case-insensitive matching by the Canonicalize of the grammar in force, never by the culture.</summary>
    IgnoreCase = 4,

    /// <summary>The <c>m</c> flag: <c>^</c> and <c>$</c> also match at line terminators.</summary>
    Multiline = 8,

    /// <summary>The <c>s</c> flag: the dot also matches line terminators.</summary>
    DotAll = 16,

    /// <summary>The <c>u</c> flag: the grammar of Unicode mode, matched by code point.</summary>
    Unicode = 32,

    /// <summary>The <c>v</c> flag: set notation and properties of strings, matched by code point. It cannot be set with <see cref="Unicode"/>.</summary>
    UnicodeSets = 64,

    /// <summary>The <c>y</c> flag: a match only at the position the search starts from.</summary>
    Sticky = 128,
}
