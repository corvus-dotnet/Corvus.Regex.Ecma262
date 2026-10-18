// <copyright file="InvariantCaseClass.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The characters of the Basic Multilingual Plane that an ordinal comparison ignoring case makes equal to a
/// character: those with the same invariant upper case. It is what <c>IndexOf</c> with
/// <c>StringComparison.OrdinalIgnoreCase</c> tests, so a class of the pattern that is exactly such a class can be
/// searched for that way.
/// </summary>
internal static class InvariantCaseClass
{
    private static readonly object Sync = new();
    private static Dictionary<char, int[]>? classes;

    /// <summary>The class of a character, sorted.</summary>
    /// <param name="c">The character, in the Basic Multilingual Plane.</param>
    /// <returns>The class, which holds the character.</returns>
    public static int[] Of(int c)
    {
        Dictionary<char, int[]> map = Classes();
        return map.TryGetValue((char)c, out int[]? cls) ? cls : [c];
    }

    private static Dictionary<char, int[]> Classes()
    {
        lock (Sync)
        {
            if (classes is null)
            {
                Dictionary<char, List<int>> byUpper = [];
                for (int c = 0; c <= 0xFFFF; c++)
                {
                    if (c >= 0xD800 && c <= 0xDFFF)
                    {
                        continue;
                    }

                    char upper = char.ToUpperInvariant((char)c);
                    if (!byUpper.TryGetValue(upper, out List<int>? members))
                    {
                        byUpper[upper] = members = [];
                    }

                    members.Add(c);
                }

                // The ordinal comparison's own casing is not quite the invariant upper-casing (the long s upper-cases
                // to S and is not equal to s ignoring case), so each group is split by the comparison itself.
                Dictionary<char, int[]> built = [];
                foreach (KeyValuePair<char, List<int>> pair in byUpper)
                {
                    List<int> rest = pair.Value;
                    while (rest.Count > 0)
                    {
                        string representative = ((char)rest[0]).ToString();
                        List<int> cls = [];
                        List<int> others = [];
                        foreach (int member in rest)
                        {
                            if (string.Equals(((char)member).ToString(), representative, StringComparison.OrdinalIgnoreCase))
                            {
                                cls.Add(member);
                            }
                            else
                            {
                                others.Add(member);
                            }
                        }

                        if (cls.Count > 1)
                        {
                            int[] sorted = [.. cls];
                            Array.Sort(sorted);
                            foreach (int member in cls)
                            {
                                built[(char)member] = sorted;
                            }
                        }

                        rest = others;
                    }
                }

                classes = built;
            }

            return classes;
        }
    }
}
