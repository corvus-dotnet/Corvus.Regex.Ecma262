// <copyright file="MatchCollection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Every match of a pattern in a text, found as they are asked for: a global search as ECMA-262 makes one.</summary>
public sealed class MatchCollection : IReadOnlyList<Match>
{
    private readonly List<Match> matches = [];
    private Match last;
    private bool done;

    internal MatchCollection(Match first)
    {
        this.last = first;
        if (first.Success)
        {
            this.matches.Add(first);
        }
        else
        {
            this.done = true;
        }
    }

    /// <summary>Gets the number of matches, which finds them all.</summary>
    public int Count
    {
        get
        {
            this.FindAll();
            return this.matches.Count;
        }
    }

    /// <inheritdoc/>
    public Match this[int index]
    {
        get
        {
            while (this.matches.Count <= index && this.FindNext())
            {
            }

            if (index < 0 || index >= this.matches.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return this.matches[index];
        }
    }

    /// <inheritdoc/>
    public IEnumerator<Match> GetEnumerator()
    {
        for (int k = 0; ; k++)
        {
            while (this.matches.Count <= k && this.FindNext())
            {
            }

            if (k >= this.matches.Count)
            {
                yield break;
            }

            yield return this.matches[k];
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private void FindAll()
    {
        while (this.FindNext())
        {
        }
    }

    private bool FindNext()
    {
        if (this.done)
        {
            return false;
        }

        Match next = this.last.NextMatch();
        if (!next.Success)
        {
            this.done = true;
            return false;
        }

        this.matches.Add(next);
        this.last = next;
        return true;
    }
}
