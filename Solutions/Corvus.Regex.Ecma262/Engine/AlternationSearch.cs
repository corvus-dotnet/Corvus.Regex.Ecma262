// <copyright file="AlternationSearch.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The search for any of the alternatives of an alternation of literals, exact or in the forms of their
/// characters (a letter's cases under the i flag): over UTF-16 the runtime's search for any of several strings,
/// over UTF-8 the multi-literal needle; the forms a search leaves out (the long s and the Kelvin sign over UTF-8,
/// whose encoding is longer than the letter's; the forms the runtime's comparison does not know over UTF-16)
/// looked for apart; and the match at a position verified alternative by alternative, in order.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class AlternationSearch
{
    /// <summary>The cost of the multi-literal needle by its model (see <see cref="MultiNeedle{T}.Cost"/>) below which it beats the runtime's search for any of several strings over UTF-16.</summary>
    private const double NeedleOverStrings = 0.35;

    private AlternationSearch(string[] literals, byte[][] utf8, int[][][]? forms, Needle<byte>[] needles8, Needle<ushort>[] needles16, int[] extras8, int[] extras16, int[] stringsExtras16, int window8, int window16)
    {
        this.Literals = literals;
        this.LiteralsUtf8 = utf8;
        this.Forms = forms;
        this.NeedlesUtf8 = needles8;
        this.NeedlesUtf16 = needles16;
        this.MultiUtf8 = new MultiNeedle<byte>(needles8);
        this.MultiUtf16 = new MultiNeedle<ushort>(needles16);
        this.ExtrasUtf8 = extras8.Length == 0 ? null : Needles.Multi<byte>(Array.ConvertAll(extras8, Rune), true, leadOnly: true);
        this.WindowUtf8 = window8;
        this.WindowUtf16 = window16;
#if NET
        // The runtime's search for any of several strings is vectorised (Teddy) for ASCII literals and far slower
        // than the needle for others, so it is kept for ASCII; and the needle beats it where the needle's anchors
        // are rare enough (two or three literals starting with capitals: Sher|Hol, Sherlock|Street, Holmes|Watson
        // measured at 0.53 to 0.65 of it; Sherlock|Holmes|Watson at 1.23, any case-insensitive search at 4 or more).
        bool ascii = true;
        foreach (string literal in literals)
        {
            foreach (char c in literal)
            {
                ascii &= c < 0x80;
            }
        }

        int shortest8 = int.MaxValue;
        int shortest16 = int.MaxValue;
        foreach (Needle<byte> needle in needles8)
        {
            shortest8 = Math.Min(shortest8, needle.Length);
        }

        foreach (Needle<ushort> needle in needles16)
        {
            shortest16 = Math.Min(shortest16, needle.Length);
        }

        // ASCII literals go to the runtime's search (exact or ignoring case as it knows it: over the low bytes of
        // UTF-16 units the fingerprint search looks at twice the lanes, and three names under the i flag measured
        // 0.11 ms against the runtime's 0.07); others to the fingerprint search where the needle would compare
        // two positions at every vector (five Russian names under the i flag: 0.18 ms against the needle's 0.67
        // and the runtime's 7.8), and to the needle where one position's span search is enough (five CJK names:
        // the needle's 24 us against 40 by fingerprints). Over UTF-8 the fingerprint search.
        bool fingerprints16 = Teddy<ushort>.Supported && needles16.Length <= Teddy<ushort>.MostLiterals && shortest16 >= 2 && this.MultiUtf16.Two;
        this.Strings = ascii && this.MultiUtf16.Cost >= NeedleOverStrings ? System.Buffers.SearchValues.Create(literals, forms is null ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) : null;
        this.TeddyUtf16 = this.Strings is null && fingerprints16 ? new Teddy<ushort>(needles16) : null;
        this.TeddyUtf8 = Teddy<byte>.Supported && needles8.Length <= Teddy<byte>.MostLiterals && shortest8 >= 2 && this.MultiUtf8.Cost >= NeedleOverStrings ? new Teddy<byte>(needles8) : null;
        int[] apart16 = this.Strings is not null ? stringsExtras16 : extras16;
#else
        int[] apart16 = extras16;
#endif
        this.ExtrasUtf16 = apart16.Length == 0 ? null : Needles.Multi<ushort>(Array.ConvertAll(apart16, Rune), false, leadOnly: true);
    }

    /// <summary>Gets the alternatives as UTF-16, each in its own form.</summary>
    public string[] Literals { get; }

    /// <summary>Gets the alternatives as UTF-8, each in its own form.</summary>
    public byte[][] LiteralsUtf8 { get; }

    /// <summary>Gets the alternatives as the forms of their characters, or null when they are matched exactly.</summary>
    public int[][][]? Forms { get; }

    /// <summary>Gets the alternatives as UTF-8 needles.</summary>
    public Needle<byte>[] NeedlesUtf8 { get; }

    /// <summary>Gets the alternatives as UTF-16 needles.</summary>
    public Needle<ushort>[] NeedlesUtf16 { get; }

    /// <summary>Gets the search for any of the alternatives at once over UTF-8.</summary>
    public MultiNeedle<byte> MultiUtf8 { get; }

    /// <summary>Gets the search for any of the alternatives at once over UTF-16 (the runtime's is preferred where it exists).</summary>
    public MultiNeedle<ushort> MultiUtf16 { get; }

    /// <summary>Gets the search for the forms the UTF-8 search leaves out, or null.</summary>
    public MultiNeedle<byte>? ExtrasUtf8 { get; }

    /// <summary>Gets the search for the forms the UTF-16 search leaves out, or null.</summary>
    public MultiNeedle<ushort>? ExtrasUtf16 { get; }

    /// <summary>Gets the most UTF-8 units an alternative can take in any of its forms.</summary>
    public int WindowUtf8 { get; }

    /// <summary>Gets the most UTF-16 units an alternative can take in any of its forms.</summary>
    public int WindowUtf16 { get; }

#if NET
    /// <summary>Gets the runtime's search for any of the alternatives over UTF-16, for ASCII literals, exact or ignoring case as the runtime knows it, or null.</summary>
    public System.Buffers.SearchValues<string>? Strings { get; }

    /// <summary>Gets the fingerprint search for any of the alternatives over UTF-8, or null where the needle is cheaper.</summary>
    public Teddy<byte>? TeddyUtf8 { get; }

    /// <summary>Gets the fingerprint search for any of the alternatives over UTF-16, or null where the needle or the runtime's search is taken.</summary>
    public Teddy<ushort>? TeddyUtf16 { get; }
#endif

    /// <summary>The search for an alternation of literals matched exactly.</summary>
    /// <param name="literals">The alternatives, none empty.</param>
    /// <returns>The search.</returns>
    public static AlternationSearch Create(string[] literals)
    {
        byte[][] utf8 = new byte[literals.Length][];
        Needle<byte>[] needles8 = new Needle<byte>[literals.Length];
        Needle<ushort>[] needles16 = new Needle<ushort>[literals.Length];
        for (int k = 0; k < literals.Length; k++)
        {
            utf8[k] = Encoding.UTF8.GetBytes(literals[k]);
            needles8[k] = new Needle<byte>(utf8[k]);
            needles16[k] = Needles.Utf16(literals[k]);
        }

        return new AlternationSearch(literals, utf8, null, needles8, needles16, [], [], [], 0, 0);
    }

    /// <summary>The search for an alternation of literals in the forms of their characters, or null when some alternative cannot be searched for by anchors.</summary>
    /// <param name="forms">For each alternative, for each character, its forms as code points, the pattern's own first.</param>
    /// <returns>The search, or null.</returns>
    public static AlternationSearch? CreateForms(int[][][] forms)
    {
        int n = forms.Length;
        string[] literals = new string[n];
        byte[][] utf8 = new byte[n][];
        Needle<byte>[] needles8 = new Needle<byte>[n];
        Needle<ushort>[] needles16 = new Needle<ushort>[n];
        List<int> extras8 = [];
        List<int> extras16 = [];
        List<int> stringsExtras16 = [];
        int window8 = 0;
        int window16 = 0;
        for (int k = 0; k < n; k++)
        {
            needles8[k] = Needles.Forms<byte>(forms[k], true, out int[] e8, out int w8);
            needles16[k] = Needles.Forms<ushort>(forms[k], false, out int[] e16, out int w16);
            if (!needles8[k].CanSearch || !needles16[k].CanSearch)
            {
                return null;
            }

            extras8.AddRange(e8);
            extras16.AddRange(e16);
            window8 = Math.Max(window8, w8);
            window16 = Math.Max(window16, w16);

            // The alternative in the pattern's own form, for the runtime's search ignoring case; a form the
            // runtime's comparison does not know (the long s for s, the theta symbol for theta) is an extra of
            // that search.
            StringBuilder sb = new();
            foreach (int[] character in forms[k])
            {
                Compiler.AppendRune(sb, character[0]);
                foreach (int form in character)
                {
                    if (!SameUpper(form, character[0]))
                    {
                        stringsExtras16.Add(form);
                    }
                }
            }

            literals[k] = sb.ToString();
            utf8[k] = Encoding.UTF8.GetBytes(literals[k]);
        }

        return new AlternationSearch(literals, utf8, forms, needles8, needles16, extras8.Distinct().ToArray(), extras16.Distinct().ToArray(), extras16.Concat(stringsExtras16).Distinct().ToArray(), window8, window16);
    }

    /// <summary>Whether two characters are one to the runtime's ordinal comparison ignoring case, for characters of one UTF-16 unit.</summary>
    private static bool SameUpper(int a, int b)
    {
        if (a > 0xFFFF || b > 0xFFFF)
        {
            return a == b;
        }

        return string.Equals(((char)a).ToString(), ((char)b).ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Rune(int codePoint)
    {
        StringBuilder sb = new();
        Compiler.AppendRune(sb, codePoint);
        return sb.ToString();
    }
}
