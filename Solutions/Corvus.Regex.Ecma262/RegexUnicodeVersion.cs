// <copyright file="RegexUnicodeVersion.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The version of Unicode whose character database a pattern is read with: the members of the <c>\p{...}</c>
/// escapes, the characters of a group name, and the case folding of the <c>i</c> flag. It is independent of the
/// <see cref="RegexEdition"/>.
/// </summary>
public enum RegexUnicodeVersion
{
    /// <summary>The version of the V8 that the answers are held to, Unicode 17 today. It moves when that V8 moves.</summary>
    Default = 0,

    /// <summary>Unicode 13.0, the version of the year of ECMAScript 2020.</summary>
    V13 = 13,

    /// <summary>Unicode 17.0.</summary>
    V17 = 17,

    /// <summary>Unicode 18.0.</summary>
    V18 = 18,

    /// <summary>The latest version this library carries, Unicode 18 today. It moves when a later version is added.</summary>
    Latest = int.MaxValue,
}
