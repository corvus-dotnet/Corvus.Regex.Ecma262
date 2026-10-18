// <copyright file="CaptureCollection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The captures of a group: one when the group took part, none otherwise.</summary>
public sealed class CaptureCollection : IReadOnlyList<Capture>
{
    private readonly Group group;

    internal CaptureCollection(Group group)
    {
        this.group = group;
    }

    /// <inheritdoc/>
    public int Count => this.group.Success ? 1 : 0;

    /// <inheritdoc/>
    public Capture this[int index] => index == 0 && this.group.Success ? this.group : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    public IEnumerator<Capture> GetEnumerator()
    {
        if (this.group.Success)
        {
            yield return this.group;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
