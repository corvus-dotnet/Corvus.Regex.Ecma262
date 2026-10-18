// <copyright file="Group.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>What one capturing group of a match captured.</summary>
public class Group : Capture
{
    private CaptureCollection? captures;

    /// <summary>Initializes a new instance of the <see cref="Group"/> class.</summary>
    /// <param name="text">The input.</param>
    /// <param name="index">The start of the capture, or -1 when the group did not take part.</param>
    /// <param name="length">The length of the capture.</param>
    /// <param name="name">The name of the group, or its number as text.</param>
    internal Group(string text, int index, int length, string name)
        : base(text, index, length)
    {
        this.Success = index >= 0;
        this.Name = name;
    }

    /// <summary>Gets a value indicating whether the group took part in the match. ECMA-262 does not tell an empty capture from a group that did not take part by the text alone; this does.</summary>
    public bool Success { get; }

    /// <summary>Gets the name of the group, or its number as text when it has no name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the captures of the group: one, when the group took part, and none otherwise. ECMA-262 keeps only the
    /// last iteration's capture of a group under a quantifier, so there is never more than one.
    /// </summary>
    public CaptureCollection Captures => this.captures ??= new CaptureCollection(this);
}
