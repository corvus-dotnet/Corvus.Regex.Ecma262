// <copyright file="EcmaRegexValidator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Buffers;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Decides whether a pattern is valid in the grammar of ECMA-262, as <see cref="EcmaRegexParser"/> decides it, with
/// nothing allocated: no tree, no sets, no string. It reads the pattern where it lies, UTF-16 or UTF-8, decoding a
/// character only where the grammar needs it, keeps the groups and the nested classes of the v flag on stacks of
/// its own rather than recursing (so no pattern can overflow the stack), and keeps a group name as its place in the
/// pattern. It asks the grammar only: the parser's limits of size (the nesting of groups, the steps it takes) belong
/// to compiling a pattern, and none of them applies here.
/// </summary>
/// <remarks>
/// Each method follows the method of the parser with the same name, and makes the same decision at each step. A
/// position is an index of a UTF-16 code unit, or for UTF-8 twice the index of a byte, plus one for the second half
/// of a character beyond the Basic Multilingual Plane when it is read as two UTF-16 code units (the grammar of no
/// flag reads code units).
/// </remarks>
internal ref struct EcmaRegexValidator
{
    /// <summary>A character escape that is not valid.</summary>
    private const int Invalid = -2;

    private const int KindGroup = 0;
    private const int KindLookahead = 1;
    private const int KindLookbehind = 2;

    // A frame of a class of the v flag: whether it is negated, its phase, its operator, whether it may contain strings.
    private const int ClassFrame = 4;
    private const int PhaseStart = 0;
    private const int PhaseUnion = 1;
    private const int PhaseOperands = 2;
    private const int WaitingFirst = 3;
    private const int WaitingUnion = 4;
    private const int WaitingOperand = 5;

    private readonly ReadOnlySpan<char> chars;
    private readonly ReadOnlySpan<byte> bytes;
    private readonly bool utf8;
    private readonly int end;
    private readonly bool unicode;
    private readonly bool unicodeSets;
    private readonly bool es2020;
    private readonly UnicodeTables tables;

    private int i;
    private bool hasNames;
    private int groupCount;
    private int disjunctions;

    // The alternatives that enclose the position, as disjunction and alternative (the parser's path).
    private IntStack path;

    // The groups open at the position: the kind of each.
    private IntStack frames;

    // Each named group: the start and the end of its name, the start and the length of its path in namePaths, and the
    // hash of its name.
    private IntStack names;
    private IntStack namePaths;

    // Each name, by its hash: one more than the place in names of the last group with that name (0 for none). A table
    // of open addressing, a power of two long and at most half full.
    private Span<int> nameTable;
    private int[]? rentedNameTable;
    private int distinctNames;

    // Each named reference: the start and the end of its name.
    private IntStack references;

    // The classes of the v flag open at the position (see ClassFrame).
    private IntStack classes;

    private EcmaRegexValidator(ReadOnlySpan<char> chars, ReadOnlySpan<byte> bytes, bool utf8, bool unicode, bool unicodeSets, RegexEdition edition, RegexUnicodeVersion unicodeVersion, Span<int> scratch)
    {
        this.chars = chars;
        this.bytes = bytes;
        this.utf8 = utf8;
        this.end = utf8 ? bytes.Length * 2 : chars.Length;
        this.unicode = unicode || unicodeSets;
        this.unicodeSets = unicodeSets;
        this.es2020 = edition == RegexEdition.Es2020;
        this.tables = UnicodeTables.For(unicodeVersion);
        this.i = 0;
        this.hasNames = false;
        this.groupCount = 0;
        this.disjunctions = 0;
        this.path = new IntStack(scratch.Slice(0, 64));
        this.frames = new IntStack(scratch.Slice(64, 32));
        this.names = new IntStack(scratch.Slice(96, 64));
        this.namePaths = new IntStack(scratch.Slice(160, 64));
        this.references = new IntStack(scratch.Slice(224, 32));
        this.classes = new IntStack(scratch.Slice(256, 64));
        this.nameTable = scratch.Slice(320, 32);
        this.nameTable.Clear();
        this.rentedNameTable = null;
        this.distinctNames = 0;
    }

    /// <summary>Whether a UTF-16 pattern is valid in the grammar.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="unicode">Whether to read it with the grammar of the u flag.</param>
    /// <param name="unicodeSets">Whether to read it with the grammar of the v flag.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValid(ReadOnlySpan<char> pattern, bool unicode, bool unicodeSets, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        Span<int> scratch = stackalloc int[352];
        EcmaRegexValidator validator = new(pattern, default, false, unicode, unicodeSets, edition, unicodeVersion, scratch);
        try
        {
            return validator.Validate();
        }
        finally
        {
            validator.Dispose();
        }
    }

    /// <summary>Whether a UTF-8 pattern is valid in the grammar. Bytes that are not UTF-8 are not a pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="unicode">Whether to read it with the grammar of the u flag.</param>
    /// <param name="unicodeSets">Whether to read it with the grammar of the v flag.</param>
    /// <param name="edition">The edition of the grammar.</param>
    /// <param name="unicodeVersion">The version of Unicode.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValid(ReadOnlySpan<byte> pattern, bool unicode, bool unicodeSets, RegexEdition edition, RegexUnicodeVersion unicodeVersion)
    {
        if (Utf8Check.FirstInvalid(pattern) >= 0)
        {
            return false;
        }

        Span<int> scratch = stackalloc int[352];
        EcmaRegexValidator validator = new(default, pattern, true, unicode, unicodeSets, edition, unicodeVersion, scratch);
        try
        {
            return validator.Validate();
        }
        finally
        {
            validator.Dispose();
        }
    }

    private static bool IsDigit(int c) => c >= '0' && c <= '9';

    private static int HexValue(int c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }

    private static bool IsOneOf(int c, string set) => c >= 0 && c < 128 && set.IndexOf((char)c) >= 0;

    private void Dispose()
    {
        this.path.Dispose();
        this.frames.Dispose();
        this.names.Dispose();
        this.namePaths.Dispose();
        this.references.Dispose();
        this.classes.Dispose();
        if (this.rentedNameTable is not null)
        {
            ArrayPool<int>.Shared.Return(this.rentedNameTable);
            this.rentedNameTable = null;
        }
    }

    /// <summary>The element at a position (a code point with the u flag's grammar, a UTF-16 code unit without), and the position after it.</summary>
    private readonly int At(int pos, out int next)
    {
        if (!this.utf8)
        {
            char c = this.chars[pos];
            if (this.unicode && char.IsHighSurrogate(c) && pos + 1 < this.chars.Length && char.IsLowSurrogate(this.chars[pos + 1]))
            {
                next = pos + 2;
                return char.ConvertToUtf32(c, this.chars[pos + 1]);
            }

            next = pos + 1;
            return c;
        }

        int b = pos >> 1;
        int first = this.bytes[b];
        int width;
        int cp;
        if (first < 0x80)
        {
            width = 1;
            cp = first;
        }
        else if (first < 0xE0)
        {
            width = 2;
            cp = ((first & 0x1F) << 6) | (this.bytes[b + 1] & 0x3F);
        }
        else if (first < 0xF0)
        {
            width = 3;
            cp = ((first & 0x0F) << 12) | ((this.bytes[b + 1] & 0x3F) << 6) | (this.bytes[b + 2] & 0x3F);
        }
        else
        {
            width = 4;
            cp = ((first & 0x07) << 18) | ((this.bytes[b + 1] & 0x3F) << 12) | ((this.bytes[b + 2] & 0x3F) << 6) | (this.bytes[b + 3] & 0x3F);
        }

        if (cp > 0xFFFF && !this.unicode)
        {
            // Two code units: the high surrogate, then the low one.
            if ((pos & 1) == 0)
            {
                next = pos | 1;
                return 0xD800 + ((cp - 0x10000) >> 10);
            }

            next = (b + width) << 1;
            return 0xDC00 + ((cp - 0x10000) & 0x3FF);
        }

        next = (b + width) << 1;
        return cp;
    }

    private readonly bool More() => this.i < this.end;

    private readonly int Peek() => this.i < this.end ? this.At(this.i, out _) : -1;

    /// <summary>The element after the one at the position, or -1.</summary>
    private readonly int PeekSecond()
    {
        if (this.i >= this.end)
        {
            return -1;
        }

        this.At(this.i, out int next);
        return next < this.end ? this.At(next, out _) : -1;
    }

    private int Next()
    {
        int c = this.At(this.i, out int next);
        this.i = next;
        return c;
    }

    private bool Eat(int c)
    {
        if (this.i < this.end && this.At(this.i, out int next) == c)
        {
            this.i = next;
            return true;
        }

        return false;
    }

    private bool Validate()
    {
        this.ScanGroups();
        this.PushDisjunction();
        while (this.More())
        {
            int c = this.Peek();
            if (c == '|')
            {
                this.Next();
                this.path[this.path.Count - 1]++;
                continue;
            }

            if (c == ')')
            {
                // The end of a group: an Unmatched ')' when none is open.
                if (this.frames.Count == 0)
                {
                    return false;
                }

                this.Next();
                this.path.Count -= 2;
                int kind = this.frames.Pop();

                // Annex B lets a quantifier follow a lookahead. The u flag does not, and neither lets one follow a
                // lookbehind.
                bool quantifiable = (kind != KindLookahead && kind != KindLookbehind) || (!this.unicode && kind == KindLookahead);
                int repeat = this.Quantifier();
                if (repeat < 0 || (repeat > 0 && !quantifiable))
                {
                    return false;
                }

                continue;
            }

            if (c == '(')
            {
                int kind = this.GroupOrLook();
                if (kind < 0)
                {
                    return false;
                }

                this.frames.Push(kind);
                this.PushDisjunction();
                continue;
            }

            if (!this.Term(c, out bool canRepeat))
            {
                return false;
            }

            int quantifier = this.Quantifier();
            if (quantifier < 0 || (quantifier > 0 && !canRepeat))
            {
                return false;
            }
        }

        // An Unterminated group.
        if (this.frames.Count != 0)
        {
            return false;
        }

        // Every named reference names a group somewhere in the pattern.
        for (int r = 0; r < this.references.Count; r += 2)
        {
            if (!this.FindName(this.references[r], this.references[r + 1], this.NameHash(this.references[r], this.references[r + 1]), out _))
            {
                return false;
            }
        }

        return true;
    }

    private void PushDisjunction()
    {
        this.path.Push(++this.disjunctions);
        this.path.Push(0);
    }

    /// <summary>Counts the capturing groups of the pattern and notes whether any is named.</summary>
    private void ScanGroups()
    {
        bool inClass = false;
        int pos = 0;
        while (pos < this.end)
        {
            int c = this.At(pos, out int next);
            if (c == '\\')
            {
                if (next < this.end)
                {
                    this.At(next, out next);
                }
            }
            else if (c == '[')
            {
                inClass = true;
            }
            else if (c == ']')
            {
                inClass = false;
            }
            else if (c == '(' && !inClass)
            {
                int after1 = this.end;
                int e1 = next < this.end ? this.At(next, out after1) : -1;
                if (e1 != '?')
                {
                    this.groupCount++;
                }
                else
                {
                    int after2 = this.end;
                    int e2 = after1 < this.end ? this.At(after1, out after2) : -1;
                    int e3 = e2 >= 0 && after2 < this.end ? this.At(after2, out _) : -1;
                    if (e2 == '<' && e3 >= 0 && e3 != '=' && e3 != '!')
                    {
                        this.groupCount++;
                        this.hasNames = true;
                    }
                }
            }

            pos = next;
        }
    }

    private bool Term(int c, out bool quantifiable)
    {
        quantifiable = true;
        switch (c)
        {
            case '^':
            case '$':
                this.Next();
                quantifiable = false;
                return true;
            case '.':
                this.Next();
                return true;
            case '[':
                return this.unicodeSets ? this.ClassSetExpression() : this.CharacterClass();
            case '\\':
                return this.AtomEscape(out quantifiable);
            case '*':
            case '+':
            case '?':
                return false;
            case '{':
                if (this.unicode || this.LooksLikeQuantifier())
                {
                    return false;
                }

                this.Next();
                return true;
            case ']':
            case '}':
                if (this.unicode)
                {
                    return false;
                }

                this.Next();
                return true;
            default:
                this.Next();
                return true;
        }
    }

    /// <summary>Whether the text at the current '{' reads as <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>.</summary>
    private readonly bool LooksLikeQuantifier()
    {
        this.At(this.i, out int k);
        int start = k;
        int c = -1;
        while (k < this.end && IsDigit(c = this.At(k, out int next)))
        {
            k = next;
        }

        if (k == start || k >= this.end)
        {
            return false;
        }

        c = this.At(k, out int afterDigits);
        if (c == '}')
        {
            return true;
        }

        if (c != ',')
        {
            return false;
        }

        k = afterDigits;
        while (k < this.end && IsDigit(this.At(k, out int next)))
        {
            k = next;
        }

        return k < this.end && this.At(k, out _) == '}';
    }

    /// <summary>Compares two runs of decimal digits (by position) as numbers, whatever their lengths.</summary>
    private readonly int CompareDigits(int aStart, int aEnd, int bStart, int bEnd)
    {
        while (aStart < aEnd && this.At(aStart, out _) == '0')
        {
            this.At(aStart, out aStart);
        }

        while (bStart < bEnd && this.At(bStart, out _) == '0')
        {
            this.At(bStart, out bStart);
        }

        // Digits are one element each, of one unit, or of two positions in UTF-8.
        int step = this.utf8 ? 2 : 1;
        int aLength = (aEnd - aStart) / step;
        int bLength = (bEnd - bStart) / step;
        if (aLength != bLength)
        {
            return aLength.CompareTo(bLength);
        }

        for (int k = 0; k < aLength; k++)
        {
            int a = this.At(aStart + (k * step), out _);
            int b = this.At(bStart + (k * step), out _);
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        return 0;
    }

    /// <summary>Reads a run of decimal digits, which saturates at <see cref="EcmaRegexParser.MaxCount"/>; -1 when there is none.</summary>
    private int Number()
    {
        int start = this.i;
        long v = 0;
        while (this.More() && IsDigit(this.Peek()))
        {
            v = Math.Min((v * 10) + (this.Next() - '0'), EcmaRegexParser.MaxCount);
        }

        return start == this.i ? -1 : (int)v;
    }

    /// <summary>Reads a quantifier: 0 when there is none, 1 when there is one, -1 when it is not valid.</summary>
    private int Quantifier()
    {
        int c = this.Peek();
        if (c == '*' || c == '+' || c == '?')
        {
            this.Next();
        }
        else if (c == '{' && this.LooksLikeQuantifier())
        {
            this.Next();
            int minStart = this.i;
            this.Number();
            int minEnd = this.i;
            bool outOfOrder = false;
            if (this.Eat(','))
            {
                if (this.Peek() != '}')
                {
                    // The counts are compared as the numbers written, which can be past what a count is kept as.
                    int maxStart = this.i;
                    this.Number();
                    outOfOrder = this.CompareDigits(maxStart, this.i, minStart, minEnd) < 0;
                }
            }

            if (!this.Eat('}') || outOfOrder)
            {
                return -1;
            }
        }
        else
        {
            return 0;
        }

        this.Eat('?');
        return 1;
    }

    /// <summary>Reads the start of a group or a lookaround, to its body: its kind, or -1 when it is not valid.</summary>
    private int GroupOrLook()
    {
        this.Next();
        if (!this.Eat('?'))
        {
            return KindGroup;
        }

        if (this.Eat(':'))
        {
            return KindGroup;
        }

        int c = this.Peek();
        if ((c == 'i' || c == 'm' || c == 's' || c == '-') && !this.es2020)
        {
            return this.ModifierGroup() ? KindGroup : -1;
        }

        if (this.Eat('=') || this.Eat('!'))
        {
            return KindLookahead;
        }

        if (this.Eat('<'))
        {
            if (this.Eat('=') || this.Eat('!'))
            {
                return KindLookbehind;
            }

            if (!this.GroupName(out int start, out int stop))
            {
                return -1;
            }

            // Two groups may share a name only when they are in separate alternatives of one disjunction, so that
            // no match can go through both. The 2020 grammar refuses a name used twice, wherever the groups are.
            // The last group with the name is the only one to ask: the groups before it lie in alternatives apart
            // from it, of a disjunction this group is either outside, where its alternatives are all one to this
            // group, or inside, in an alternative after theirs.
            int hash = this.NameHash(start, stop);
            bool used = this.FindName(start, stop, hash, out int slot);
            if (used)
            {
                int last = this.nameTable[slot] - 1;
                if (this.es2020 || !this.SeparateAlternatives(this.names[last + 2], this.names[last + 3]))
                {
                    return -1;
                }
            }

            int entry = this.names.Count;
            this.names.Push(start);
            this.names.Push(stop);
            this.names.Push(this.namePaths.Count);
            this.names.Push(this.path.Count);
            this.names.Push(hash);
            for (int k = 0; k < this.path.Count; k++)
            {
                this.namePaths.Push(this.path[k]);
            }

            if (used)
            {
                this.nameTable[slot] = entry + 1;
            }
            else
            {
                this.AddName(slot, entry);
            }

            return KindGroup;
        }

        return -1;
    }

    /// <summary>
    /// Whether a named group (by the start and the length of its path) and the current position lie in different
    /// alternatives of some disjunction.
    /// </summary>
    private readonly bool SeparateAlternatives(int otherStart, int otherLength)
    {
        for (int k = 0; (2 * k) + 1 < otherLength && (2 * k) + 1 < this.path.Count && this.namePaths[otherStart + (2 * k)] == this.path[2 * k]; k++)
        {
            if (this.namePaths[otherStart + (2 * k) + 1] != this.path[(2 * k) + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the modifiers of a group such as <c>(?i:...)</c> or <c>(?s-i:...)</c>, after the "(?", to its ':'.</summary>
    private bool ModifierGroup()
    {
        int seen = 0;
        bool removing = false;
        while (!this.Eat(':'))
        {
            int c = this.Peek();
            if (c == '-')
            {
                if (removing)
                {
                    return false;
                }

                this.Next();
                removing = true;
                continue;
            }

            int flag = c == 'i' ? 1 : c == 'm' ? 2 : c == 's' ? 4 : 0;
            if (flag == 0 || (seen & flag) != 0)
            {
                return false;
            }

            this.Next();
            seen |= flag;
        }

        return seen != 0;
    }

    /// <summary>
    /// Reads a group name up to and including its '>', and gives the positions of its first character and of the
    /// '>'. A character of the name may be written as a Unicode escape.
    /// </summary>
    private bool GroupName(out int start, out int stop)
    {
        start = this.i;
        stop = this.i;
        int count = 0;
        while (true)
        {
            if (this.Peek() == '>')
            {
                stop = this.i;
                this.Next();
                break;
            }

            if (!this.More())
            {
                return false;
            }

            int c = this.NameCharacter(out bool ok);
            if (!ok || (count == 0 ? !this.tables.IsNameStart(c) : !this.tables.IsNamePart(c)))
            {
                return false;
            }

            count++;
        }

        return count != 0;
    }

    /// <summary>Reads one character of a group name: a code point, written or as a Unicode escape.</summary>
    private int NameCharacter(out bool ok)
    {
        ok = true;
        int c = this.Next();
        if (c == '\\')
        {
            if (!this.Eat('u'))
            {
                ok = false;
                return -1;
            }

            c = this.UnicodeEscape(braces: true, pairs: true);
            ok = c >= 0;
        }
        else if (!this.unicode && c >= 0xD800 && c <= 0xDBFF && this.More() && this.Peek() >= 0xDC00 && this.Peek() <= 0xDFFF)
        {
            // A name is made of code points whatever the grammar.
            c = char.ConvertToUtf32((char)c, (char)this.Next());
        }

        return c;
    }

    /// <summary>The hash of a name, by the positions of its characters, over its code points.</summary>
    private int NameHash(int start, int stop)
    {
        int saved = this.i;
        uint hash = 2166136261;
        this.i = start;
        while (this.i < stop)
        {
            hash = (hash ^ (uint)this.NameCharacter(out _)) * 16777619;
        }

        this.i = saved;
        return (int)hash;
    }

    /// <summary>
    /// Finds a name in the table: whether a group has it, and the slot that holds its last group, or the empty slot
    /// it would take.
    /// </summary>
    private bool FindName(int start, int stop, int hash, out int slot)
    {
        int mask = this.nameTable.Length - 1;
        for (slot = hash & mask; ; slot = (slot + 1) & mask)
        {
            int held = this.nameTable[slot];
            if (held == 0)
            {
                return false;
            }

            int n = held - 1;
            if (this.names[n + 4] == hash && this.SameName(start, stop, this.names[n], this.names[n + 1]))
            {
                return true;
            }
        }
    }

    /// <summary>Puts a new name's group in the empty slot found for it, and doubles the table when it is half full.</summary>
    private void AddName(int slot, int entry)
    {
        this.nameTable[slot] = entry + 1;
        if (++this.distinctNames * 2 <= this.nameTable.Length)
        {
            return;
        }

        int[] bigger = ArrayPool<int>.Shared.Rent(this.nameTable.Length * 2);
        Span<int> table = bigger.AsSpan(0, this.nameTable.Length * 2);
        table.Clear();
        int mask = table.Length - 1;
        foreach (int held in this.nameTable)
        {
            if (held != 0)
            {
                int s = this.names[held - 1 + 4] & mask;
                while (table[s] != 0)
                {
                    s = (s + 1) & mask;
                }

                table[s] = held;
            }
        }

        if (this.rentedNameTable is not null)
        {
            ArrayPool<int>.Shared.Return(this.rentedNameTable);
        }

        this.rentedNameTable = bigger;
        this.nameTable = table;
    }

    /// <summary>Whether two names, by the positions of their characters, are the same code points.</summary>
    private bool SameName(int aStart, int aStop, int bStart, int bStop)
    {
        int saved = this.i;
        int a = aStart;
        int b = bStart;
        bool same = true;
        while (same)
        {
            bool aDone = a >= aStop;
            bool bDone = b >= bStop;
            if (aDone || bDone)
            {
                same = aDone && bDone;
                break;
            }

            this.i = a;
            int ca = this.NameCharacter(out _);
            a = this.i;
            this.i = b;
            int cb = this.NameCharacter(out _);
            b = this.i;
            same = ca == cb;
        }

        this.i = saved;
        return same;
    }

    /// <summary>Reads an escape outside a class.</summary>
    private bool AtomEscape(out bool quantifiable)
    {
        quantifiable = true;
        this.Next();
        if (!this.More())
        {
            return false;
        }

        int c = this.Peek();
        if (c == 'b' || c == 'B')
        {
            this.Next();
            quantifiable = false;
            return true;
        }

        if (c == 'k')
        {
            this.Next();

            // Annex B reads \k as the letter k in a pattern with no named group.
            if (!this.unicode && !this.hasNames)
            {
                return true;
            }

            if (!this.Eat('<') || !this.GroupName(out int start, out int stop))
            {
                return false;
            }

            this.references.Push(start);
            this.references.Push(stop);
            return true;
        }

        if (c >= '1' && c <= '9')
        {
            int start = this.i;
            int n = this.Number();
            if (n <= this.groupCount)
            {
                return true;
            }

            if (this.unicode)
            {
                return false;
            }

            // Annex B reads it as an octal escape, or as the digit itself.
            this.i = start;
            this.LegacyOctal();
            return true;
        }

        int escape = this.ClassEscape(c, out _);
        if (escape != 0)
        {
            return escape > 0;
        }

        return this.CharacterEscape(inClass: false) != Invalid;
    }

    /// <summary>
    /// Reads an Annex B octal escape of up to three digits with a value below 256. A digit that cannot start one
    /// (8 or 9) stands for itself.
    /// </summary>
    private int LegacyOctal()
    {
        int v = 0;
        int k = 0;
        while (k < 3 && this.More() && this.Peek() >= '0' && this.Peek() <= '7' && (v * 8) + (this.Peek() - '0') <= 255)
        {
            v = (v * 8) + (this.Next() - '0');
            k++;
        }

        return k == 0 ? this.Next() : v;
    }

    /// <summary>
    /// Reads a class escape such as <c>\d</c> or <c>\p{...}</c> whose letter is <paramref name="c"/>: 1 when it is one
    /// (with whether it is a property of strings), -1 when it is not valid, 0, reading nothing, when <paramref name="c"/>
    /// does not start one.
    /// </summary>
    private int ClassEscape(int c, out bool strings)
    {
        strings = false;
        switch (c)
        {
            case 'd':
            case 'D':
            case 'w':
            case 'W':
            case 's':
            case 'S':
                this.Next();
                return 1;
            case 'p':
            case 'P':
            {
                // Without the u flag \p is the letter p.
                if (!this.unicode)
                {
                    return 0;
                }

                this.Next();
                if (!this.Eat('{'))
                {
                    return -1;
                }

                int start = this.i;
                int length = 0;
                while (this.More() && this.Peek() != '}')
                {
                    this.Next();
                    length++;
                }

                if (!this.Eat('}'))
                {
                    return -1;
                }

                // No property name or value is near this long, so a longer expression names none.
                if (length > 128)
                {
                    return -1;
                }

                Span<char> expression = stackalloc char[length];
                int pos = start;
                for (int k = 0; k < length; k++)
                {
                    // A property name is ASCII, so a character beyond it only has to stay a character that is in no name.
                    int e = this.At(pos, out pos);
                    expression[k] = e > 0xFFFF ? (char)0xFFFF : (char)e;
                }

                // The 2020 grammar knows only the names and values of its own tables, whatever the version of Unicode.
                bool found = !(this.es2020 && !Es2020Properties.Has(expression)) && this.tables.HasProperty(expression);
                if (!found && this.unicodeSets && this.tables.IsStringProperty(expression))
                {
                    // A property of strings, which the v flag alone names, and which has no complement.
                    if (c == 'P')
                    {
                        return -1;
                    }

                    strings = true;
                    return 1;
                }

                return found ? 1 : -1;
            }

            default:
                return 0;
        }
    }

    /// <summary>Reads exactly <paramref name="digits"/> hexadecimal digits. Returns -1, reading nothing, when they are not there.</summary>
    private int Hex(int digits)
    {
        int pos = this.i;
        int v = 0;
        for (int k = 0; k < digits; k++)
        {
            if (pos >= this.end)
            {
                return -1;
            }

            int d = HexValue(this.At(pos, out pos));
            if (d < 0)
            {
                return -1;
            }

            v = (v * 16) + d;
        }

        this.i = pos;
        return v;
    }

    /// <summary>Reads the escape after a backslash, in or out of a class: the character it names, or <see cref="Invalid"/>.</summary>
    private int CharacterEscape(bool inClass)
    {
        int at = this.i;
        int c = this.Next();
        switch (c)
        {
            case 'f':
                return '\f';
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            case 'v':
                return 0x0b;
            case 'c':
            {
                int l = this.Peek();
                if ((l >= 'a' && l <= 'z') || (l >= 'A' && l <= 'Z'))
                {
                    this.Next();
                    return l % 32;
                }

                if (!this.unicode && inClass && (IsDigit(l) || l == '_'))
                {
                    this.Next();
                    return l % 32;
                }

                if (this.unicode)
                {
                    return Invalid;
                }

                // Annex B reads a \c that is not a control escape as a backslash followed by the letter c.
                this.i = at;
                return '\\';
            }

            case '0':
                if (this.More() && IsDigit(this.Peek()))
                {
                    if (this.unicode)
                    {
                        return Invalid;
                    }

                    this.i = at;
                    return this.LegacyOctal();
                }

                return 0;
            case 'x':
            {
                int h = this.Hex(2);
                return h >= 0 ? h : this.unicode ? Invalid : 'x';
            }

            case 'u':
            {
                int u = this.UnicodeEscape(braces: this.unicode, pairs: this.unicode);
                return u >= 0 ? u : this.unicode ? Invalid : 'u';
            }

            default:
                break;
        }

        if (c >= '1' && c <= '9' && !this.unicode && inClass)
        {
            this.i = at;
            return this.LegacyOctal();
        }

        // Identity escapes. The u flag allows only the syntax characters and '/', and '-' in a class. Annex B
        // allows any character, but k in a pattern with a named group.
        if ((this.unicode && !IsOneOf(c, "^$\\.*+?()[]{}|/") && !(inClass && c == '-'))
            || (c == 'k' && this.hasNames))
        {
            return Invalid;
        }

        return c;
    }

    /// <summary>
    /// Reads what follows the u of a Unicode escape and returns the character, or -1, reading nothing, when it is
    /// not a well-formed escape. With <paramref name="braces"/> it accepts the form with braces. With
    /// <paramref name="pairs"/> a surrogate pair written as two escapes is one code point.
    /// </summary>
    private int UnicodeEscape(bool braces, bool pairs)
    {
        int start = this.i;
        if (braces && this.Eat('{'))
        {
            int digits = this.i;
            int v = 0;
            while (this.More() && HexValue(this.Peek()) >= 0)
            {
                if (v <= EcmaRegexUnicode.Max)
                {
                    v = (v * 16) + HexValue(this.Peek());
                }

                this.Next();
            }

            if (digits == this.i || !this.Eat('}') || v > EcmaRegexUnicode.Max)
            {
                this.i = start;
                return -1;
            }

            return v;
        }

        int u = this.Hex(4);
        if (u < 0)
        {
            return -1;
        }

        if (pairs && u >= 0xD800 && u <= 0xDBFF && this.Peek() == '\\' && this.PeekSecond() == 'u')
        {
            int save = this.i;
            this.Next();
            this.Next();
            int low = this.Hex(4);
            if (low >= 0xDC00 && low <= 0xDFFF)
            {
                return char.ConvertToUtf32((char)u, (char)low);
            }

            this.i = save;
        }

        return u;
    }

    /// <summary>Reads a character class from its '[' to its ']'.</summary>
    private bool CharacterClass()
    {
        this.Next();
        this.Eat('^');
        while (true)
        {
            if (!this.More())
            {
                return false;
            }

            if (this.Eat(']'))
            {
                return true;
            }

            int first = this.ClassAtom(out bool firstSet);
            if (first == Invalid)
            {
                return false;
            }

            if (this.Peek() == '-' && this.PeekSecond() >= 0 && this.PeekSecond() != ']')
            {
                this.Next();
                int second = this.ClassAtom(out bool secondSet);
                if (second == Invalid)
                {
                    return false;
                }

                if (firstSet || secondSet)
                {
                    // Annex B reads the '-' as itself when a class escape is at either end.
                    if (this.unicode)
                    {
                        return false;
                    }

                    continue;
                }

                if (second < first)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>Reads one member of a class: its character, or 0 with <paramref name="set"/> for a class escape, or <see cref="Invalid"/>.</summary>
    private int ClassAtom(out bool set)
    {
        set = false;
        int c = this.Next();
        if (c != '\\')
        {
            return c;
        }

        if (!this.More())
        {
            return Invalid;
        }

        int e = this.Peek();
        if (e == 'b')
        {
            this.Next();
            return '\b';
        }

        int escape = this.ClassEscape(e, out _);
        if (escape != 0)
        {
            set = true;
            return escape > 0 ? 0 : Invalid;
        }

        return this.CharacterEscape(inClass: true);
    }

    /// <summary>
    /// Reads a class of the v flag's grammar from its '[' to its ']', with its nested classes on a stack of frames: a
    /// union of elements, or the intersection or the difference of operands, which may not be mixed. A negated class
    /// may not hold anything that may match a string.
    /// </summary>
    private bool ClassSetExpression()
    {
        this.OpenClass();
        while (true)
        {
            int top = this.classes.Count - ClassFrame;
            int phase = this.classes[top + 1];
            int element;
            bool mayContainStrings;
            bool range;
            switch (phase)
            {
                case PhaseStart:
                    if (this.Peek() == ']')
                    {
                        this.classes[top + 3] = 0;
                        if (!this.CloseClass(out bool done))
                        {
                            return false;
                        }

                        if (done)
                        {
                            return true;
                        }

                        continue;
                    }

                    element = this.ClassSetElement(out mayContainStrings, out range);
                    if (element < 0)
                    {
                        return false;
                    }

                    if (element > 0)
                    {
                        this.classes[top + 1] = WaitingFirst;
                        continue;
                    }

                    if (!this.FirstOperand(top, mayContainStrings, range))
                    {
                        return false;
                    }

                    continue;

                case PhaseUnion:
                    if (this.More() && this.Peek() != ']')
                    {
                        if (this.SetOperator() != 0)
                        {
                            return false;
                        }

                        element = this.ClassSetElement(out mayContainStrings, out _);
                        if (element < 0)
                        {
                            return false;
                        }

                        if (element > 0)
                        {
                            this.classes[top + 1] = WaitingUnion;
                        }
                        else if (mayContainStrings)
                        {
                            this.classes[top + 3] = 1;
                        }

                        continue;
                    }

                    if (!this.CloseClass(out bool unionDone))
                    {
                        return false;
                    }

                    if (unionDone)
                    {
                        return true;
                    }

                    continue;

                case PhaseOperands:
                    if (this.SetOperator() == this.classes[top + 2])
                    {
                        this.Next();
                        this.Next();
                        if (this.Peek() == ']' || !this.More())
                        {
                            return false;
                        }

                        element = this.ClassSetElement(out mayContainStrings, out range);
                        if (element < 0 || range)
                        {
                            return false;
                        }

                        if (element > 0)
                        {
                            this.classes[top + 1] = WaitingOperand;
                        }
                        else
                        {
                            this.Operand(top, mayContainStrings);
                        }

                        continue;
                    }

                    if (this.Peek() != ']')
                    {
                        return false;
                    }

                    if (!this.CloseClass(out bool operandsDone))
                    {
                        return false;
                    }

                    if (operandsDone)
                    {
                        return true;
                    }

                    continue;

                default:
                    // A nested class is open above this one; it is never the top while it waits.
                    return false;
            }
        }
    }

    private void OpenClass()
    {
        this.Next();
        bool negated = this.Eat('^');
        this.classes.Push(negated ? 1 : 0);
        this.classes.Push(PhaseStart);
        this.classes.Push(0);
        this.classes.Push(0);
    }

    /// <summary>
    /// Closes the class at the top at its ']', and gives what it may contain to the class it is nested in, if any
    /// (with <paramref name="done"/> when it was the outermost).
    /// </summary>
    private bool CloseClass(out bool done)
    {
        done = false;
        if (!this.Eat(']'))
        {
            return false;
        }

        int top = this.classes.Count - ClassFrame;
        bool negated = this.classes[top] != 0;
        bool mayContainStrings = this.classes[top + 3] != 0;
        if (negated && mayContainStrings)
        {
            return false;
        }

        this.classes.Count -= ClassFrame;
        if (this.classes.Count == 0)
        {
            done = true;
            return true;
        }

        // A nested class is an operand, never a range; a negated one may contain no strings.
        mayContainStrings &= !negated;
        int parent = this.classes.Count - ClassFrame;
        switch (this.classes[parent + 1])
        {
            case WaitingFirst:
                return this.FirstOperand(parent, mayContainStrings, range: false);
            case WaitingUnion:
                this.classes[parent + 1] = PhaseUnion;
                if (mayContainStrings)
                {
                    this.classes[parent + 3] = 1;
                }

                return true;
            default:
                this.classes[parent + 1] = PhaseOperands;
                this.Operand(parent, mayContainStrings);
                return true;
        }
    }

    /// <summary>Takes the first element of a class, and decides from what follows it whether the class is a union or operations.</summary>
    private bool FirstOperand(int top, bool mayContainStrings, bool range)
    {
        this.classes[top + 3] = mayContainStrings ? 1 : 0;
        int op = this.SetOperator();
        if (op != 0)
        {
            // An intersection or a difference: operands only, with the one operator between every two.
            if (range)
            {
                return false;
            }

            this.classes[top + 1] = PhaseOperands;
            this.classes[top + 2] = op;
            return true;
        }

        this.classes[top + 1] = PhaseUnion;
        return true;
    }

    /// <summary>Takes an operand of an intersection (strings only where both may hold them) or a difference (the first's).</summary>
    private void Operand(int top, bool mayContainStrings)
    {
        if (this.classes[top + 2] == '&' && !mayContainStrings)
        {
            this.classes[top + 3] = 0;
        }
    }

    /// <summary>The set operator at the position ('&amp;' for &amp;&amp;, '-' for --), or 0.</summary>
    private readonly int SetOperator()
    {
        int c = this.Peek();
        return (c == '&' || c == '-') && this.PeekSecond() == c ? c : 0;
    }

    /// <summary>
    /// Reads one element of a class of the v flag's grammar: 0 for an operand or a range of two characters, 1 when it
    /// is a nested class (whose frame is now open), -1 when it is not valid.
    /// </summary>
    private int ClassSetElement(out bool mayContainStrings, out bool range)
    {
        mayContainStrings = false;
        range = false;
        int c = this.Peek();
        if (c == '[')
        {
            this.OpenClass();
            return 1;
        }

        if (c == '\\')
        {
            int e = this.PeekSecond();
            if (e < 0)
            {
                return -1;
            }

            if (e == 'q')
            {
                this.Next();
                this.Next();
                return this.ClassStringDisjunction(out mayContainStrings) ? 0 : -1;
            }

            int save = this.i;
            this.Next();
            int escape = this.ClassEscape(e, out bool strings);
            if (escape < 0)
            {
                return -1;
            }

            if (escape > 0)
            {
                mayContainStrings = strings;
                return 0;
            }

            this.i = save;
        }

        int first = this.ClassSetCharacter();
        if (first == Invalid)
        {
            return -1;
        }

        if (this.Peek() == '-' && this.SetOperator() == 0)
        {
            this.Next();
            int p = this.Peek();
            if (p == ']' || p == '[' || (p == '\\' && IsOneOf(this.PeekSecond(), "qdDwWsSpP")))
            {
                return -1;
            }

            int second = this.ClassSetCharacter();
            if (second == Invalid || second < first)
            {
                return -1;
            }

            range = true;
        }

        return 0;
    }

    /// <summary>
    /// Reads one character of a class of the v flag's grammar: a character that is not a syntax character and does
    /// not begin a reserved double punctuator, or an escape, which may be of a reserved punctuator.
    /// </summary>
    private int ClassSetCharacter()
    {
        if (!this.More())
        {
            return Invalid;
        }

        int c = this.Peek();
        if (c == '\\')
        {
            int e = this.PeekSecond();
            if (e < 0)
            {
                return Invalid;
            }

            if (e == 'b')
            {
                this.Next();
                this.Next();
                return '\b';
            }

            if (IsOneOf(e, "&-!#%,:;<=>@`~"))
            {
                this.Next();
                this.Next();
                return e;
            }

            this.Next();
            return this.CharacterEscape(inClass: true);
        }

        if (IsOneOf(c, "()[]{}/-\\|"))
        {
            return Invalid;
        }

        if (this.PeekSecond() == c && IsOneOf(c, "&!#$%*+,.:;<=>?@^`~"))
        {
            return Invalid;
        }

        this.Next();
        return c;
    }

    /// <summary>Reads the strings of <c>\q{...}</c> after its <c>\q</c>: whether any is of other than one character.</summary>
    private bool ClassStringDisjunction(out bool mayContainStrings)
    {
        mayContainStrings = false;
        if (!this.Eat('{'))
        {
            return false;
        }

        int count = 0;
        while (true)
        {
            if (!this.More())
            {
                return false;
            }

            int c = this.Peek();
            if (c == '|' || c == '}')
            {
                this.Next();
                if (count != 1)
                {
                    mayContainStrings = true;
                }

                count = 0;
                if (c == '}')
                {
                    return true;
                }

                continue;
            }

            if (this.ClassSetCharacter() == Invalid)
            {
                return false;
            }

            count++;
        }
    }

    /// <summary>A stack of integers on memory the caller gives, which takes a larger array from the pool when it outgrows it.</summary>
    private ref struct IntStack
    {
        private Span<int> items;
        private int[]? rented;

        public IntStack(Span<int> initial)
        {
            this.items = initial;
            this.rented = null;
            this.Count = 0;
        }

        public int Count { get; set; }

        public readonly ref int this[int index] => ref this.items[index];

        public void Push(int value)
        {
            if (this.Count == this.items.Length)
            {
                this.Grow();
            }

            this.items[this.Count++] = value;
        }

        public int Pop() => this.items[--this.Count];

        public void Dispose()
        {
            if (this.rented is not null)
            {
                ArrayPool<int>.Shared.Return(this.rented);
                this.rented = null;
            }
        }

        private void Grow()
        {
            int[] bigger = ArrayPool<int>.Shared.Rent(Math.Max(16, this.items.Length * 2));
            this.items.Slice(0, this.Count).CopyTo(bigger);
            if (this.rented is not null)
            {
                ArrayPool<int>.Shared.Return(this.rented);
            }

            this.rented = bigger;
            this.items = bigger;
        }
    }
}
