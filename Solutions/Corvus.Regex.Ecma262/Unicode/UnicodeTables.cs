// <copyright file="UnicodeTables.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The Unicode of one version for ECMA-262 patterns: the sets of the <c>\p{...}</c> escapes, the characters of a
/// group name, and the case folding of a case-insensitive group. The data of each version is generated from its
/// Unicode Character Database (<see cref="EcmaRegexUnicodeData17"/> and <see cref="EcmaRegexUnicodeStrings17"/> for
/// 17.0), so a pattern means the same whichever runtime compiles it and whichever runs it.
/// </summary>
/// <remarks>
/// A set is read from its table the first time it is asked for and then kept. Nothing here runs while a string is
/// matched, but for the case folding of a case-insensitive backreference (<see cref="Canonicalize"/>).
/// </remarks>
internal sealed class UnicodeTables
{
    // What a property expression names: nothing, a General_Category value, a binary property, a script by its Script
    // property, or a script by its Script_Extensions property. The index of the name is in the low bits.
    private const int None = -1;
    private const int Category = 1 << 16;
    private const int Binary = 2 << 16;
    private const int Script = 3 << 16;
    private const int ScriptExtensions = 4 << 16;
    private const int NoCharacter = 5 << 16;

    private const string RgiEmoji = "RGI_Emoji";

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz+/";

    /// <summary>
    /// The characters of the Basic Multilingual Plane whose full uppercase form is more than one character, which
    /// the Canonicalize of a pattern with no flag leaves alone (they are the single characters of SpecialCasing.txt).
    /// </summary>
    private static readonly int[] MultipleUppercase =
    [
        0xDF, 0xDF, 0x149, 0x149, 0x1F0, 0x1F0, 0x390, 0x390, 0x3B0, 0x3B0, 0x587, 0x587, 0x1E96, 0x1E9A, 0x1F50,
        0x1F50, 0x1F52, 0x1F52, 0x1F54, 0x1F54, 0x1F56, 0x1F56, 0x1F80, 0x1FAF, 0x1FB2, 0x1FB4, 0x1FB6, 0x1FB7, 0x1FBC,
        0x1FBC, 0x1FC2, 0x1FC4, 0x1FC6, 0x1FC7, 0x1FCC, 0x1FCC, 0x1FD2, 0x1FD3, 0x1FD6, 0x1FD7, 0x1FE2, 0x1FE4, 0x1FE6,
        0x1FE7, 0x1FF2, 0x1FF4, 0x1FF6, 0x1FF7, 0x1FFC, 0x1FFC, 0xFB00, 0xFB06, 0xFB13, 0xFB17,
    ];

    private static UnicodeTables? v13;
    private static UnicodeTables? v17;
    private static UnicodeTables? v18;

    private readonly int[]?[] tables;
    private readonly Func<int, string> table;
    private readonly string simpleFolding;
    private readonly string uppercase;
    private readonly string[] categoryNames;
    private readonly int[] categoryTables;
    private readonly string[] binaryNames;
    private readonly int[] binaryTables;
    private readonly string[] scriptNames;
    private readonly int[] scriptTables;
    private readonly int[] scriptExtensionTables;
    private readonly string[] stringNames;
    private readonly Func<int, string> stringCodePoints;
    private readonly Func<int, string> stringSequences;
    private readonly Dictionary<int, int[]> categories = [];
    private readonly object sync = new();
    private int[]? idStart;
    private int[]? idContinue;
    private Folding? unicodeFolding;
    private int[]? simpleFoldingRuns;
    private int[]? uppercaseRuns;
    private Folding? legacyFolding;
    private (int[] Set, int[][] Strings)[]? stringProperties;

    private UnicodeTables(
        RegexUnicodeVersion version,
        int tableCount,
        Func<int, string> table,
        string simpleFolding,
        string uppercase,
        string[] categoryNames,
        int[] categoryTables,
        string[] binaryNames,
        int[] binaryTables,
        string[] scriptNames,
        int[] scriptTables,
        int[] scriptExtensionTables,
        string[] stringNames,
        Func<int, string> stringCodePoints,
        Func<int, string> stringSequences)
    {
        this.Version = version;
        this.tables = new int[tableCount][];
        this.table = table;
        this.simpleFolding = simpleFolding;
        this.uppercase = uppercase;
        this.categoryNames = categoryNames;
        this.categoryTables = categoryTables;
        this.binaryNames = binaryNames;
        this.binaryTables = binaryTables;
        this.scriptNames = scriptNames;
        this.scriptTables = scriptTables;
        this.scriptExtensionTables = scriptExtensionTables;
        this.stringNames = stringNames;
        this.stringCodePoints = stringCodePoints;
        this.stringSequences = stringSequences;
    }

    /// <summary>Gets the tables of Unicode 13.0.</summary>
    public static UnicodeTables V13 => v13 ??= new(
        RegexUnicodeVersion.V13,
        EcmaRegexUnicodeData13.TableCount,
        EcmaRegexUnicodeData13.Table,
        EcmaRegexUnicodeData13.SimpleFolding,
        EcmaRegexUnicodeData13.Uppercase,
        EcmaRegexUnicodeData13.CategoryNames,
        EcmaRegexUnicodeData13.CategoryTables,
        EcmaRegexUnicodeData13.BinaryNames,
        EcmaRegexUnicodeData13.BinaryTables,
        EcmaRegexUnicodeData13.ScriptNames,
        EcmaRegexUnicodeData13.ScriptTables,
        EcmaRegexUnicodeData13.ScriptExtensionTables,
        EcmaRegexUnicodeStrings13.Names,
        EcmaRegexUnicodeStrings13.CodePoints,
        EcmaRegexUnicodeStrings13.Sequences);

    /// <summary>Gets the tables of Unicode 17.0.</summary>
    public static UnicodeTables V17 => v17 ??= new(
        RegexUnicodeVersion.V17,
        EcmaRegexUnicodeData17.TableCount,
        EcmaRegexUnicodeData17.Table,
        EcmaRegexUnicodeData17.SimpleFolding,
        EcmaRegexUnicodeData17.Uppercase,
        EcmaRegexUnicodeData17.CategoryNames,
        EcmaRegexUnicodeData17.CategoryTables,
        EcmaRegexUnicodeData17.BinaryNames,
        EcmaRegexUnicodeData17.BinaryTables,
        EcmaRegexUnicodeData17.ScriptNames,
        EcmaRegexUnicodeData17.ScriptTables,
        EcmaRegexUnicodeData17.ScriptExtensionTables,
        EcmaRegexUnicodeStrings17.Names,
        EcmaRegexUnicodeStrings17.CodePoints,
        EcmaRegexUnicodeStrings17.Sequences);

    /// <summary>Gets the tables of Unicode 18.0.</summary>
    public static UnicodeTables V18 => v18 ??= new(
        RegexUnicodeVersion.V18,
        EcmaRegexUnicodeData18.TableCount,
        EcmaRegexUnicodeData18.Table,
        EcmaRegexUnicodeData18.SimpleFolding,
        EcmaRegexUnicodeData18.Uppercase,
        EcmaRegexUnicodeData18.CategoryNames,
        EcmaRegexUnicodeData18.CategoryTables,
        EcmaRegexUnicodeData18.BinaryNames,
        EcmaRegexUnicodeData18.BinaryTables,
        EcmaRegexUnicodeData18.ScriptNames,
        EcmaRegexUnicodeData18.ScriptTables,
        EcmaRegexUnicodeData18.ScriptExtensionTables,
        EcmaRegexUnicodeStrings18.Names,
        EcmaRegexUnicodeStrings18.CodePoints,
        EcmaRegexUnicodeStrings18.Sequences);

    /// <summary>Gets the version of the tables, which is never <see cref="RegexUnicodeVersion.Default"/> or <see cref="RegexUnicodeVersion.Latest"/>.</summary>
    public RegexUnicodeVersion Version { get; }

    /// <summary>The version a setting resolves to: <see cref="RegexUnicodeVersion.Default"/> to V8's, 17, and <see cref="RegexUnicodeVersion.Latest"/> to 18.</summary>
    /// <param name="version">The setting.</param>
    /// <returns>The version.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The setting is not a member of <see cref="RegexUnicodeVersion"/>.</exception>
    public static RegexUnicodeVersion Resolve(RegexUnicodeVersion version) => version switch
    {
        RegexUnicodeVersion.Default => RegexUnicodeVersion.V17,
        RegexUnicodeVersion.Latest => RegexUnicodeVersion.V18,
        RegexUnicodeVersion.V13 or RegexUnicodeVersion.V17 or RegexUnicodeVersion.V18 => version,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Not a version of Unicode this library carries."),
    };

    /// <summary>The tables of a version.</summary>
    /// <param name="version">The version, which <see cref="Resolve"/> resolves.</param>
    /// <returns>The tables.</returns>
    public static UnicodeTables For(RegexUnicodeVersion version) => Resolve(version) switch
    {
        RegexUnicodeVersion.V13 => V13,
        RegexUnicodeVersion.V18 => V18,
        _ => V17,
    };

    /// <summary>
    /// The set and the strings of a property of strings, which a pattern with the v flag may name: the seven of
    /// ECMA-262's table, each the single code points and the sequences of an emoji file of the Unicode Character
    /// Database, and RGI_Emoji the union of the other six.
    /// </summary>
    /// <param name="name">The property name, which is case-sensitive.</param>
    /// <param name="set">Receives the single code points.</param>
    /// <param name="strings">Receives the sequences, as code points, each of two or more.</param>
    /// <returns><see langword="false"/> when the name is not a property of strings.</returns>
    public bool StringProperty(ReadOnlySpan<char> name, out int[] set, out int[][] strings)
    {
        set = [];
        strings = [];
        int index = this.StringPropertyIndex(name, out bool all);
        if (index < 0 && !all)
        {
            return false;
        }

        lock (this.sync)
        {
            if (this.stringProperties is null)
            {
                int n = this.stringNames.Length;
                this.stringProperties = new (int[] Set, int[][] Strings)[n + 1];
                int[] union = [];
                List<int[]> every = [];
                for (int k = 0; k < n; k++)
                {
                    int[] codePoints = ParseRanges(this.stringCodePoints(k));
                    int[][] sequences = ParseSequences(this.stringSequences(k));
                    this.stringProperties[k] = (codePoints, sequences);
                    union = EcmaRegexUnicode.Union(union, codePoints);
                    every.AddRange(sequences);
                }

                this.stringProperties[n] = (union, [.. every]);
            }

            (set, strings) = this.stringProperties[all ? this.stringNames.Length : index];
            return true;
        }
    }

    /// <summary>The code points of a text of hexadecimal ranges ("231A..231B 23F0").</summary>
    private static int[] ParseRanges(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        string[] parts = text.Split(' ');
        int[] pairs = new int[2 * parts.Length];
        for (int k = 0; k < parts.Length; k++)
        {
            int dots = parts[k].IndexOf("..", StringComparison.Ordinal);
            pairs[2 * k] = Convert.ToInt32(dots < 0 ? parts[k] : parts[k].Substring(0, dots), 16);
            pairs[(2 * k) + 1] = dots < 0 ? pairs[2 * k] : Convert.ToInt32(parts[k].Substring(dots + 2), 16);
        }

        return EcmaRegexUnicode.SetOf(pairs, pairs.Length);
    }

    /// <summary>The sequences of a text of hexadecimal code points ("1F468 200D 1F469;2122 FE0F").</summary>
    private static int[][] ParseSequences(string text)
    {
        string[] parts = text.Split(';');
        int[][] sequences = new int[parts.Length][];
        for (int k = 0; k < parts.Length; k++)
        {
            string[] points = parts[k].Split(' ');
            sequences[k] = new int[points.Length];
            for (int j = 0; j < points.Length; j++)
            {
                sequences[k][j] = Convert.ToInt32(points[j], 16);
            }
        }

        return sequences;
    }

    /// <summary>Whether a name is a property of strings of this version (see <see cref="StringProperty"/>), with nothing built.</summary>
    /// <param name="name">The property name, which is case-sensitive.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsStringProperty(ReadOnlySpan<char> name)
    {
        return this.StringPropertyIndex(name, out bool all) >= 0 || all;
    }

    /// <summary>
    /// Whether the expression between the braces of <c>\p{...}</c> names a property, or a property and a value, of
    /// this version (see <see cref="Property"/>), with nothing built.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool HasProperty(ReadOnlySpan<char> expression) => this.ResolveName(expression) != None;

    /// <summary>
    /// Resolves the expression between the braces of <c>\p{...}</c> as ECMA-262 does: a name and a value of
    /// General_Category, Script or Script_Extensions joined by <c>=</c>, or the lone name of a General_Category
    /// value or of a binary property. Names are matched exactly.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="categoryTables">For a General_Category value, the tables it is the union of, one bit to a table, and otherwise 0.</param>
    /// <returns>The set of the property, or <see langword="null"/> when ECMA-262 does not define it.</returns>
    public int[]? Property(ReadOnlySpan<char> expression, out int categoryTables)
    {
        categoryTables = 0;
        int resolved = this.ResolveName(expression);
        if (resolved == None)
        {
            return null;
        }

        int index = resolved & 0xFFFF;
        switch (resolved & ~0xFFFF)
        {
            case Category:
                categoryTables = this.categoryTables[index];
                return this.CategorySet(categoryTables);
            case Binary:
                return this.Table(this.binaryTables[index]);
            case Script:
                return this.Table(this.scriptTables[index]);
            case NoCharacter:
                return [];
            default:
                return this.Table(this.scriptExtensionTables[index]);
        }
    }

    /// <summary>Whether a code point may start a group name: ID_Start, <c>$</c> or <c>_</c>.</summary>
    /// <param name="c">The code point.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    public bool IsNameStart(int c)
    {
        if (c < 128)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '$' || c == '_';
        }

        this.idStart ??= this.Property("ID_Start".AsSpan(), out _);
        return EcmaRegexUnicode.Contains(this.idStart!, c);
    }

    /// <summary>Whether a code point may continue a group name: ID_Continue, <c>$</c>, U+200C or U+200D.</summary>
    /// <param name="c">The code point.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    public bool IsNamePart(int c)
    {
        if (c < 128)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '$' || c == '_';
        }

        if (c == 0x200C || c == 0x200D)
        {
            return true;
        }

        this.idContinue ??= this.Property("ID_Continue".AsSpan(), out _);
        return EcmaRegexUnicode.Contains(this.idContinue!, c);
    }

    /// <summary>
    /// The set of every character equivalent to some member of the set, under the Canonicalize of the u flag grammar
    /// (<paramref name="unicode"/>) or of a pattern with no flag.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="unicode">Whether the pattern is read with the u flag grammar.</param>
    /// <returns>The set with every equivalent character added, which is <paramref name="set"/> itself when there is none to add.</returns>
    /// <remarks>
    /// ECMA-262 defines case-insensitive matching through a Canonicalize function: two characters match when they
    /// canonicalize to the same character. With the u flag grammar it is Unicode simple case folding. With no flag it
    /// is the uppercase mapping of one UTF-16 code unit, when that is again one code unit, and it never maps a
    /// character outside ASCII into ASCII.
    /// </remarks>
    public int[] FoldClosure(int[] set, bool unicode)
    {
        Folding f = this.GetFolding(unicode);
        int[] extra = new int[16];
        int m = 0;
        long taken = 1 + (set.Length / 8);
        for (int i = 0; i < set.Length; i += 2)
        {
            int k = Array.BinarySearch(f.Points, set[i]);
            if (k < 0)
            {
                k = ~k;
            }

            for (; k < f.Points.Length && f.Points[k] <= set[i + 1]; k++)
            {
                taken += f.Classes[k].Length;
                foreach (int member in f.Classes[k])
                {
                    if (!EcmaRegexUnicode.Contains(set, member))
                    {
                        if (m + 2 > extra.Length)
                        {
                            Array.Resize(ref extra, extra.Length * 2);
                        }

                        extra[m++] = member;
                        extra[m++] = member;
                    }
                }
            }
        }

        EcmaRegexUnicode.Spend(taken / 2);
        if (m == 0)
        {
            return set;
        }

        Array.Resize(ref extra, m);
        return EcmaRegexUnicode.Union(set, extra);
    }

    /// <summary>
    /// The classes of Canonicalize that have more than one member and a member in a set.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="unicode">Whether the pattern is read with the u flag grammar.</param>
    /// <returns>Each class once, as its members in order.</returns>
    public List<int[]> FoldClasses(int[] set, bool unicode)
    {
        Folding f = this.GetFolding(unicode);
        List<int[]> classes = [];
        HashSet<int> seen = [];
        for (int i = 0; i < set.Length; i += 2)
        {
            int k = Array.BinarySearch(f.Points, set[i]);
            if (k < 0)
            {
                k = ~k;
            }

            for (; k < f.Points.Length && f.Points[k] <= set[i + 1]; k++)
            {
                EcmaRegexUnicode.Spend(1);
                int[] members = (int[])f.Classes[k].Clone();
                Array.Sort(members);
                if (seen.Add(members[0]))
                {
                    classes.Add(members);
                }
            }
        }

        return classes;
    }

    /// <summary>The characters that are equivalent to some other character under the Canonicalize of a grammar.</summary>
    /// <param name="unicode">Whether the pattern is read with the u flag grammar.</param>
    /// <returns>The set.</returns>
    public int[] Cased(bool unicode) => this.GetFolding(unicode).Set;

    /// <summary>
    /// The Canonicalize function of ECMA-262 for case-insensitive matching, which a backreference needs when it is
    /// matched. With the u flag grammar it is Unicode simple case folding. With no flag it is the uppercase mapping
    /// of one UTF-16 code unit, when that is again one code unit, and it never maps a character outside ASCII into
    /// ASCII. A character beyond the Basic Multilingual Plane is two code units, and neither has an uppercase form.
    /// </summary>
    /// <param name="c">The code point.</param>
    /// <param name="unicode">Whether the pattern is read with the u flag grammar.</param>
    /// <returns>The canonical code point.</returns>
    public int Canonicalize(int c, bool unicode)
    {
        if (unicode)
        {
            return Map(this.simpleFoldingRuns ??= Mapping(this.simpleFolding), c);
        }

        if (c > 0xFFFF || EcmaRegexUnicode.Contains(MultipleUppercase, c))
        {
            return c;
        }

        int upper = Map(this.uppercaseRuns ??= Mapping(this.uppercase), c);
        if ((c >= 128 && upper < 128) || upper > 0xFFFF)
        {
            return c;
        }

        return upper;
    }

    /// <summary>The set of a table of the generated data.</summary>
    private int[] Table(int number)
    {
        lock (this.sync)
        {
            int[]? set = this.tables[number];
            if (set is null)
            {
                string text = this.table(number);
                int[] result = new int[text.Length];
                int m = 0;
                int at = 0;
                int next = 0;
                while (at < text.Length)
                {
                    int lo = next + Number(text, ref at);
                    int hi = lo + Number(text, ref at);
                    result[m++] = lo;
                    result[m++] = hi;
                    next = hi + 1;
                }

                Array.Resize(ref result, m);
                set = result;
                this.tables[number] = set;
            }

            return set;
        }
    }

    /// <summary>Reads one number of a table's text at <paramref name="at"/>, which it moves past the number.</summary>
    private static int Number(string text, ref int at)
    {
        int value = 0;
        int shift = 0;
        while (true)
        {
            int digit = Alphabet.IndexOf(text[at++]);
            value |= (digit & 31) << shift;
            if (digit < 32)
            {
                return value;
            }

            shift += 5;
        }
    }

    /// <summary>The index of a property of strings among <see cref="stringNames"/>, or -1, with whether the name is RGI_Emoji, the union of them.</summary>
    private int StringPropertyIndex(ReadOnlySpan<char> name, out bool all)
    {
        int index = -1;
        for (int k = 0; k < this.stringNames.Length; k++)
        {
            if (name.SequenceEqual(this.stringNames[k].AsSpan()))
            {
                index = k;
            }
        }

        all = index < 0 && name.SequenceEqual(RgiEmoji.AsSpan());
        return index;
    }

    private static int Find(string[] names, ReadOnlySpan<char> name)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (name.SequenceEqual(names[i].AsSpan()))
            {
                return i;
            }
        }

        return -1;
    }

    private int ResolveName(ReadOnlySpan<char> expression)
    {
        int eq = expression.IndexOf('=');
        if (eq < 0)
        {
            int category = Find(this.categoryNames, expression);
            if (category >= 0)
            {
                return Category | category;
            }

            int binary = Find(this.binaryNames, expression);
            return binary >= 0 ? Binary | binary : None;
        }

        ReadOnlySpan<char> name = expression.Slice(0, eq);
        ReadOnlySpan<char> value = expression.Slice(eq + 1);
        if (name.SequenceEqual("General_Category".AsSpan()) || name.SequenceEqual("gc".AsSpan()))
        {
            int category = Find(this.categoryNames, value);
            return category >= 0 ? Category | category : None;
        }

        if (name.SequenceEqual("Script".AsSpan()) || name.SequenceEqual("sc".AsSpan()))
        {
            int script = Find(this.scriptNames, value);
            return script >= 0 ? Script | script : IsScriptOfNoCharacter(value) ? NoCharacter : None;
        }

        if (name.SequenceEqual("Script_Extensions".AsSpan()) || name.SequenceEqual("scx".AsSpan()))
        {
            int script = Find(this.scriptNames, value);
            return script >= 0 ? ScriptExtensions | script : IsScriptOfNoCharacter(value) ? NoCharacter : None;
        }

        return None;
    }

    /// <summary>
    /// Whether a name is the Script value Katakana_Or_Hiragana or its alias Hrkt. PropertyValueAliases.txt lists
    /// it, so ECMA-262 has it, and no character has it as its Script or among its Script_Extensions, so the tables
    /// have no entry for it. V8 13.6 refuses it.
    /// </summary>
    private static bool IsScriptOfNoCharacter(ReadOnlySpan<char> value)
    {
        return value.SequenceEqual("Hrkt".AsSpan()) || value.SequenceEqual("Katakana_Or_Hiragana".AsSpan());
    }

    /// <summary>The union of the General_Category tables whose bits are set.</summary>
    private int[] CategorySet(int tables)
    {
        if ((tables & (tables - 1)) == 0)
        {
            return this.Table(TrailingZeros(tables));
        }

        lock (this.sync)
        {
            if (!this.categories.TryGetValue(tables, out int[]? set))
            {
                int length = 0;
                for (int bits = tables; bits != 0; bits &= bits - 1)
                {
                    length += this.Table(TrailingZeros(bits)).Length;
                }

                int[] pairs = new int[length];
                int m = 0;
                for (int bits = tables; bits != 0; bits &= bits - 1)
                {
                    int[] part = this.Table(TrailingZeros(bits));
                    Array.Copy(part, 0, pairs, m, part.Length);
                    m += part.Length;
                }

                set = EcmaRegexUnicode.SetOf(pairs, m);
                this.categories[tables] = set;
            }

            return set;
        }
    }

    private static int TrailingZeros(int bits)
    {
        int n = 0;
        while ((bits & 1) == 0)
        {
            bits >>= 1;
            n++;
        }

        return n;
    }

    /// <summary>A case mapping of the generated data as runs of start, length, delta and stride.</summary>
    private static int[] Mapping(string text)
    {
        int[] result = new int[text.Length];
        int m = 0;
        int at = 0;
        int next = 0;
        while (at < text.Length)
        {
            int start = next + Number(text, ref at);
            int length = Number(text, ref at) + 1;
            int delta = Number(text, ref at);
            result[m++] = start;
            result[m++] = length;
            result[m++] = (delta & 1) != 0 ? -(delta >> 1) : delta >> 1;
            result[m++] = Number(text, ref at) + 1;
            next = start + length;
        }

        Array.Resize(ref result, m);
        return result;
    }

    /// <summary>What a mapping makes of a code point.</summary>
    private static int Map(int[] runs, int c)
    {
        int lo = 0;
        int hi = runs.Length / 4;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            int start = runs[4 * mid];
            if (c < start)
            {
                hi = mid;
            }
            else if (c >= start + runs[(4 * mid) + 1])
            {
                lo = mid + 1;
            }
            else
            {
                return (c - start) % runs[(4 * mid) + 3] == 0 ? c + runs[(4 * mid) + 2] : c;
            }
        }

        return c;
    }

    private Folding GetFolding(bool unicode)
    {
        lock (this.sync)
        {
            Folding? f = unicode ? this.unicodeFolding : this.legacyFolding;
            if (f is not null)
            {
                return f;
            }

            Dictionary<int, List<int>> byKey = [];
            if (unicode)
            {
                int[] runs = Mapping(this.simpleFolding);
                for (int r = 0; r < runs.Length; r += 4)
                {
                    for (int c = runs[r]; c < runs[r] + runs[r + 1]; c += runs[r + 3])
                    {
                        int key = c + runs[r + 2];
                        Add(byKey, key, key);
                        Add(byKey, key, c);
                    }
                }
            }
            else
            {
                int[] runs = Mapping(this.uppercase);
                for (int c = 0; c <= 0xFFFF; c++)
                {
                    int upper = EcmaRegexUnicode.Contains(MultipleUppercase, c) ? c : Map(runs, c);
                    if ((c >= 128 && upper < 128) || upper > 0xFFFF)
                    {
                        upper = c;
                    }

                    Add(byKey, upper, c);
                }
            }

            int count = 0;
            foreach (List<int> members in byKey.Values)
            {
                if (members.Count > 1)
                {
                    count += members.Count;
                }
            }

            int[] points = new int[count];
            int m = 0;
            foreach (List<int> members in byKey.Values)
            {
                if (members.Count > 1)
                {
                    members.CopyTo(points, m);
                    m += members.Count;
                }
            }

            Array.Sort(points);
            int[][] classes = new int[count][];
            foreach (List<int> members in byKey.Values)
            {
                if (members.Count > 1)
                {
                    int[] shared = [.. members];
                    foreach (int member in shared)
                    {
                        classes[Array.BinarySearch(points, member)] = shared;
                    }
                }
            }

            int[] pairs = new int[2 * count];
            for (int i = 0; i < count; i++)
            {
                pairs[2 * i] = points[i];
                pairs[(2 * i) + 1] = points[i];
            }

            f = new Folding(points, classes, EcmaRegexUnicode.SetOf(pairs, pairs.Length));
            if (unicode)
            {
                this.unicodeFolding = f;
            }
            else
            {
                this.legacyFolding = f;
            }

            return f;
        }
    }

    private static void Add(Dictionary<int, List<int>> byKey, int key, int member)
    {
        if (!byKey.TryGetValue(key, out List<int>? members))
        {
            byKey[key] = [member];
            return;
        }

        if (!members.Contains(member))
        {
            members.Add(member);
        }
    }

    /// <summary>The characters that are equivalent to some other character, each with every member of its class.</summary>
    private sealed class Folding(int[] points, int[][] classes, int[] set)
    {
        /// <summary>Gets the characters, sorted.</summary>
        public int[] Points { get; } = points;

        /// <summary>Gets, for each of <see cref="Points"/>, the members of its class, itself among them.</summary>
        public int[][] Classes { get; } = classes;

        /// <summary>Gets <see cref="Points"/> as a set.</summary>
        public int[] Set { get; } = set;
    }
}
