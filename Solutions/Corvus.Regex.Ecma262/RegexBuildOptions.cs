// <copyright file="RegexBuildOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// When a regular expression builds what its matches use. These are options of the engine, not of the pattern, so
/// they are not among the <see cref="RegexFlags"/>, which are the flags of ECMAScript.
/// </summary>
[Flags]
public enum RegexBuildOptions
{
    /// <summary>
    /// The automata a match uses (the one over ASCII that decides <c>IsMatch</c>, and those that speed a search) and the
    /// program that tracks every group are built when a match first needs them, so a regular expression pays nothing
    /// for what it never does.
    /// </summary>
    None = 0,

    /// <summary>
    /// Everything is built when the regular expression is constructed, so its first match costs what a later one does.
    /// For a regular expression made once and matched many times, such as a validator's, where the time to construct
    /// it is paid once and the first match should not be slower than the rest. Like
    /// <c>System.Text.RegularExpressions.RegexOptions.Compiled</c>, it moves work to construction.
    /// </summary>
    Eager = 1,
}
