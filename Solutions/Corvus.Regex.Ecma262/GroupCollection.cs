// <copyright file="GroupCollection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>The groups of a match: the whole match as group 0, then each capturing group in the order of its opening parenthesis.</summary>
public sealed class GroupCollection : IReadOnlyList<Group>
{
    private readonly Group[] groups;
    private readonly List<KeyValuePair<string, int[]>> names;

    internal GroupCollection(Group[] groups, List<KeyValuePair<string, int[]>> names)
    {
        this.groups = groups;
        this.names = names;
    }

    /// <inheritdoc/>
    public int Count => this.groups.Length;

    /// <inheritdoc/>
    public Group this[int index] => this.groups[index];

    /// <summary>
    /// Gets the group of a name. When several groups share the name (in separate alternatives), it is the one that
    /// took part, or the first of them when none did. A name the pattern does not have gives a group that did not
    /// take part, as .NET does.
    /// </summary>
    /// <param name="name">The name, or a group number as text.</param>
    /// <returns>The group.</returns>
    public Group this[string name]
    {
        get
        {
            if (this.TryGetValue(name, out Group? group))
            {
                return group;
            }

            return new Group(this.groups[0].Text, -1, 0, name);
        }
    }

    /// <summary>Whether the pattern has a group of the name.</summary>
    /// <param name="name">The name, or a group number as text.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    public bool ContainsKey(string name) => this.TryGetValue(name, out _);

    /// <summary>Gets the group of a name (see the indexer).</summary>
    /// <param name="name">The name, or a group number as text.</param>
    /// <param name="group">The group.</param>
    /// <returns><see langword="true"/> when the pattern has a group of the name.</returns>
    public bool TryGetValue(string name, out Group group)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        foreach (KeyValuePair<string, int[]> named in this.names)
        {
            if (named.Key != name)
            {
                continue;
            }

            Group? first = null;
            foreach (int number in named.Value)
            {
                Group g = this.groups[number];
                first ??= g;
                if (g.Success)
                {
                    group = g;
                    return true;
                }
            }

            group = first!;
            return true;
        }

        if (int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0 && n < this.groups.Length)
        {
            group = this.groups[n];
            return true;
        }

        group = null!;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<Group> GetEnumerator() => ((IEnumerable<Group>)this.groups).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
