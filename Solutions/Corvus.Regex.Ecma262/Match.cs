// <copyright file="Match.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>One match of a pattern in a text, with what each of its groups captured.</summary>
public sealed class Match : Group
{
    private static readonly Match EmptyMatch = new(null, string.Empty, [-1, -1], [], 0);

    private readonly Regex? regex;
    private readonly int next;
    private GroupCollection? groups;
    private Group[]? groupArray;
    private readonly int[] captures;
    private readonly List<KeyValuePair<string, int[]>> names;

    /// <summary>Initializes a new instance of the <see cref="Match"/> class.</summary>
    /// <param name="regex">The pattern, or null for the empty match.</param>
    /// <param name="text">The input.</param>
    /// <param name="captures">The match's start and end, then each group's start and end or -1 and -1.</param>
    /// <param name="names">The group names.</param>
    /// <param name="next">The position the next search starts from.</param>
    internal Match(Regex? regex, string text, int[] captures, List<KeyValuePair<string, int[]>> names, int next)
        : base(text, captures[0], captures[1] - captures[0], "0")
    {
        this.regex = regex;
        this.captures = captures;
        this.names = names;
        this.next = next;
    }

    /// <summary>Gets the match that is no match: <see cref="Group.Success"/> is false, and every group is empty.</summary>
    public static Match Empty => EmptyMatch;

    /// <summary>Gets the groups of the match: the whole match as group 0, then each capturing group.</summary>
    public GroupCollection Groups => this.groups ??= new GroupCollection(this.GroupArray(), this.names);

    /// <summary>
    /// The next match in the input after this one, as ECMA-262 continues a global search: from the end of this
    /// match, or one character on when this match is empty. <see cref="Empty"/> when there is none.
    /// </summary>
    /// <returns>The next match.</returns>
    /// <exception cref="RegexMatchTimeoutException">The match reached its limit.</exception>
    public Match NextMatch()
    {
        if (this.regex is null || !this.Success || this.next > this.Text.Length)
        {
            return Empty;
        }

        return this.regex.MatchAt(this.Text, this.next);
    }

    /// <summary>
    /// Expands a replacement for this match as ECMA-262's GetSubstitution does: <c>$$</c>, <c>$&amp;</c>,
    /// <c>$`</c>, <c>$'</c>, <c>$n</c>, <c>$nn</c> and <c>$&lt;name&gt;</c>. Anything else is left as it is.
    /// </summary>
    /// <param name="replacement">The replacement.</param>
    /// <returns>The expansion.</returns>
    public string Result(string replacement)
    {
        if (replacement is null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        if (!this.Success)
        {
            throw new NotSupportedException("The empty match has no result.");
        }

        System.Text.StringBuilder sb = new();
        this.AppendResult(sb, replacement);
        return sb.ToString();
    }

    /// <summary>Appends the expansion of a replacement for this match (see <see cref="Result"/>).</summary>
    /// <param name="sb">Where to append it.</param>
    /// <param name="replacement">The replacement.</param>
    internal void AppendResult(System.Text.StringBuilder sb, string replacement)
    {
        string text = this.Text;
        int m = (this.captures.Length / 2) - 1;
        int k = 0;
        while (k < replacement.Length)
        {
            char c = replacement[k];
            if (c != '$' || k + 1 >= replacement.Length)
            {
                sb.Append(c);
                k++;
                continue;
            }

            char d = replacement[k + 1];
            switch (d)
            {
                case '$':
                    sb.Append('$');
                    k += 2;
                    continue;
                case '&':
                    sb.Append(text, this.Index, this.Length);
                    k += 2;
                    continue;
                case '`':
                    sb.Append(text, 0, this.Index);
                    k += 2;
                    continue;
                case '\'':
                    sb.Append(text, this.Index + this.Length, text.Length - this.Index - this.Length);
                    k += 2;
                    continue;
                case '<':
                {
                    if (this.names.Count == 0)
                    {
                        break;
                    }

                    int gt = replacement.IndexOf('>', k + 2);
                    if (gt < 0)
                    {
                        break;
                    }

                    string name = replacement.Substring(k + 2, gt - k - 2);
                    foreach (KeyValuePair<string, int[]> named in this.names)
                    {
                        if (named.Key == name)
                        {
                            foreach (int number in named.Value)
                            {
                                if (this.captures[2 * number] >= 0)
                                {
                                    sb.Append(text, this.captures[2 * number], this.captures[(2 * number) + 1] - this.captures[2 * number]);
                                    break;
                                }
                            }

                            break;
                        }
                    }

                    k = gt + 1;
                    continue;
                }

                default:
                {
                    if (d < '0' || d > '9')
                    {
                        break;
                    }

                    int digits = 1;
                    int index = d - '0';
                    if (k + 2 < replacement.Length && replacement[k + 2] >= '0' && replacement[k + 2] <= '9')
                    {
                        int two = (index * 10) + (replacement[k + 2] - '0');
                        if (two >= 1 && two <= m)
                        {
                            digits = 2;
                            index = two;
                        }
                    }

                    if (index < 1 || index > m)
                    {
                        break;
                    }

                    if (this.captures[2 * index] >= 0)
                    {
                        sb.Append(text, this.captures[2 * index], this.captures[(2 * index) + 1] - this.captures[2 * index]);
                    }

                    k += 1 + digits;
                    continue;
                }
            }

            // Not a substitution: the dollar stands for itself.
            sb.Append('$');
            k++;
        }
    }

    /// <summary>The start and end of each group, as the matcher reported them.</summary>
    internal int[] Captures0 => this.captures;

    private Group[] GroupArray()
    {
        if (this.groupArray is not null)
        {
            return this.groupArray;
        }

        int count = this.captures.Length / 2;
        Group[] groups = new Group[count];
        groups[0] = this;
        for (int g = 1; g < count; g++)
        {
            string name = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (KeyValuePair<string, int[]> named in this.names)
            {
                if (Array.IndexOf(named.Value, g) >= 0)
                {
                    name = named.Key;
                    break;
                }
            }

            int from = this.captures[2 * g];
            groups[g] = new Group(this.Text, from, from < 0 ? 0 : this.captures[(2 * g) + 1] - from, name);
        }

        this.groupArray = groups;
        return groups;
    }
}
