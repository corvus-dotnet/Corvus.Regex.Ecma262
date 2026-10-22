// <copyright file="EcmaRegexParser.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// This file is the parser of the Corvus.JsonSchema .NET translator (src/Corvus.Text.Json.CodeGeneration/EcmaRegexParser.cs),
// without what only the translator read: the order a class wrote its members in, the General_Category values .NET has
// a class for, and the measurements of the tree.

using System;
using System.Collections.Generic;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Reads an ECMA-262 pattern by recursive descent into a tree. With <c>unicode</c> it applies the grammar of the u
/// flag and reads the pattern by code point. Without it, it applies the Annex B grammar of a pattern with no flag and
/// reads the pattern by UTF-16 code unit, as such a pattern is matched. The grammar is that of ECMAScript 2025.
/// </summary>
internal sealed class EcmaRegexParser
{
    /// <summary>
    /// Groups, and the classes of the v flag's grammar that a class holds, may nest this deep together: a pattern of
    /// this many groups, each inside the last, is read, and one of a group more is not, and so for nested classes and
    /// for any mix of the two. It bounds the recursion of the reader and of the writer. A pattern that nests deeper is
    /// refused as too large (<see cref="RegexPatternTooLargeException"/>), and not as a pattern that is not valid.
    /// </summary>
    public const int MaxNesting = 200;

    /// <summary>The largest bound of a counted quantifier. No string reaches a larger count.</summary>
    public const int MaxCount = int.MaxValue - 1;

    // The classes ECMA-262 defines, as sets. \d and \w are ASCII only, \s is WhiteSpace and LineTerminator, and the
    // dot is everything but the four line terminators.
    private static readonly int[] Digit = ['0', '9'];
    private static readonly int[] Word = ['0', '9', 'A', 'Z', '_', '_', 'a', 'z'];

    /// <summary>
    /// With the u flag grammar and case-insensitive matching, <c>\w</c> and <c>\b</c> also count the two characters
    /// that fold to an ASCII letter (U+017F, the long s, and U+212A, the Kelvin sign) as word characters.
    /// </summary>
    private static readonly int[] FoldWord = ['0', '9', 'A', 'Z', '_', '_', 'a', 'z', 0x17F, 0x17F, 0x212A, 0x212A];

    private static readonly int[] Space =
    [
        0x9, 0xD, 0x20, 0x20, 0xA0, 0xA0, 0x1680, 0x1680, 0x2000, 0x200A, 0x2028, 0x2029, 0x202F, 0x202F, 0x205F,
        0x205F, 0x3000, 0x3000, 0xFEFF, 0xFEFF,
    ];

    private static readonly int[] LineTerminators = ['\n', '\n', '\r', '\r', 0x2028, 0x2029];

    // The sets that are every character but a few, which most patterns hold, worked out once for each grammar.
    private static readonly Complements CodePointComplements = new(EcmaRegexUnicode.Max);
    private static readonly Complements CodeUnitComplements = new(0xFFFF);

    private readonly int[] p;
    private readonly int max;
    private readonly Complements complements;
    private readonly EcmaRegexWork work;

    /// <summary>For each group name, its groups: the number of each, then the alternatives that enclose it.</summary>
    private readonly Dictionary<string, List<int[]>> names = [];
    private readonly List<EcmaRegexNode> namedRefs = [];
    private int i;
    /// <summary>The number of groups that enclose the disjunction being read: 0 for the pattern's own.</summary>
    private int depth = -1;
    private bool hasNames;
    private int groups;

    /// <summary>The alternatives that enclose the current position, innermost last, as disjunction and alternative.</summary>
    private int[] path = new int[16];
    private int pathLength;
    private int disjunctions;

    // The modifiers in force: the flags of the pattern, which a group such as (?i:...) changes for its body.
    private bool ignoreCase;
    private bool multiline;
    private bool dotAll;

    /// <summary>The ranges a class has gathered so far, in the order the pattern gave them.</summary>
    private int[] pairs = new int[16];
    private int pairCount;

    /// <summary>The set of the class atom <see cref="ClassAtom"/> last read, when it was a class escape, and null otherwise.</summary>
    private int[]? atomSet;

    /// <summary>The strings of the property of strings a class escape last read named, or null (the v flag only).</summary>
    private int[][]? escapeStrings;

    /// <summary>Whether the pattern is read with the grammar of the v flag: the u flag's, with set notation and strings in a class.</summary>
    private readonly bool unicodeSets;

    /// <summary>Initializes a new instance of the <see cref="EcmaRegexParser"/> class.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="unicode">Whether to read it with the grammar of the u flag.</param>
    /// <param name="ignoreCase">Whether the i flag is set: the modifiers in force at the start of the pattern.</param>
    /// <param name="multiline">Whether the m flag is set.</param>
    /// <param name="dotAll">Whether the s flag is set.</param>
    /// <param name="work">The count of the steps taken for the pattern, when the caller keeps one.</param>
    /// <param name="unicodeSets">Whether to read it with the grammar of the v flag (which implies <paramref name="unicode"/>).</param>
    public EcmaRegexParser(ReadOnlySpan<char> pattern, bool unicode, bool ignoreCase = false, bool multiline = false, bool dotAll = false, EcmaRegexWork? work = null, bool unicodeSets = false)
    {
        this.work = work ?? new EcmaRegexWork();
        this.Unicode = unicode || unicodeSets;
        this.unicodeSets = unicodeSets;
        this.ignoreCase = ignoreCase;
        this.multiline = multiline;
        this.dotAll = dotAll;
        this.max = this.Unicode ? EcmaRegexUnicode.Max : 0xFFFF;
        this.complements = this.Unicode ? CodePointComplements : CodeUnitComplements;
        int[] units = new int[pattern.Length];
        int n = 0;
        for (int k = 0; k < pattern.Length; k++)
        {
            char c = pattern[k];
            if (this.Unicode && char.IsHighSurrogate(c) && k + 1 < pattern.Length && char.IsLowSurrogate(pattern[k + 1]))
            {
                units[n++] = char.ConvertToUtf32(c, pattern[++k]);
            }
            else
            {
                units[n++] = c;
            }
        }

        if (n != units.Length)
        {
            Array.Resize(ref units, n);
        }

        this.p = units;
    }

    /// <summary>Gets a value indicating whether the pattern is read with the grammar of the u flag.</summary>
    public bool Unicode { get; }

    /// <summary>
    /// Gets the number of capturing groups, from a scan of the whole pattern before it is read, as the meaning of
    /// <c>\1</c> and <c>\k</c> depends on groups that may follow them.
    /// </summary>
    public int GroupCount { get; private set; }

    /// <summary>Gets a value indicating whether the pattern holds a backreference.</summary>
    public bool HasBackreferences { get; private set; }

    /// <summary>Gets, once the pattern is read, each group name with the numbers of its groups, in the order the pattern first used the names.</summary>
    public List<KeyValuePair<string, int[]>> GroupNames { get; } = [];

    /// <summary>Gets every character: the code points with the u flag grammar and the UTF-16 code units without.</summary>
    public int[] Any => this.complements.Any;

    /// <summary>Reads the pattern.</summary>
    /// <returns>The root of its tree.</returns>
    /// <exception cref="RegexParseException">The pattern is not valid in the grammar.</exception>
    /// <exception cref="RegexPatternTooLargeException">The pattern is over a limit of size.</exception>
    public EcmaRegexNode Parse()
    {
        this.work.Spend(this.p.Length);
        this.ScanGroups();
        EcmaRegexNode root = this.Disjunction();
        if (this.i < this.p.Length)
        {
            throw this.Error(this.p[this.i] == ')' ? "Unmatched ')'" : "Unexpected character");
        }

        foreach (KeyValuePair<string, List<int[]>> named in this.names)
        {
            int[] numbers = new int[named.Value.Count];
            for (int k = 0; k < numbers.Length; k++)
            {
                numbers[k] = named.Value[k][0];
            }

            this.GroupNames.Add(new KeyValuePair<string, int[]>(named.Key, numbers));
        }

        foreach (EcmaRegexNode reference in this.namedRefs)
        {
            if (!this.names.TryGetValue(reference.Name!, out List<int[]>? named))
            {
                throw this.Error("Reference to the unknown group name '" + reference.Name + "'");
            }

            int[] numbers = new int[named.Count];
            for (int k = 0; k < numbers.Length; k++)
            {
                numbers[k] = named[k][0];
            }

            reference.Groups = numbers;
        }

        return root;
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

    private static EcmaRegexNode SetNode(int[] set) => new(EcmaRegexNodeKind.Set) { Set = set };

    private RegexParseException Error(string reason) => new(reason, this.Offset(this.i));

    /// <summary>The offset in the pattern, in UTF-16 code units, of an index of <see cref="p"/>.</summary>
    private int Offset(int index)
    {
        if (!this.Unicode)
        {
            return index;
        }

        int offset = 0;
        for (int k = 0; k < index && k < this.p.Length; k++)
        {
            offset += this.p[k] > 0xFFFF ? 2 : 1;
        }

        return offset;
    }

    /// <summary>Counts the capturing groups of the pattern and notes whether any is named.</summary>
    private void ScanGroups()
    {
        bool inClass = false;
        for (int k = 0; k < this.p.Length; k++)
        {
            int c = this.p[k];
            if (c == '\\')
            {
                k++;
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
                if (k + 1 >= this.p.Length || this.p[k + 1] != '?')
                {
                    this.GroupCount++;
                }
                else if (k + 3 < this.p.Length && this.p[k + 2] == '<' && this.p[k + 3] != '=' && this.p[k + 3] != '!')
                {
                    this.GroupCount++;
                    this.hasNames = true;
                }
            }
        }
    }

    private bool More() => this.i < this.p.Length;

    private int Peek() => this.i < this.p.Length ? this.p[this.i] : -1;

    private bool Eat(int c)
    {
        if (this.i < this.p.Length && this.p[this.i] == c)
        {
            this.i++;
            return true;
        }

        return false;
    }

    private EcmaRegexNode Disjunction()
    {
        DeepStack.Ensure();
        if (++this.depth > MaxNesting)
        {
            throw new RegexPatternTooLargeException(
                "Its groups nest more than " + MaxNesting.ToString(System.Globalization.CultureInfo.InvariantCulture) + " deep.");
        }

        if ((2 * this.pathLength) + 2 > this.path.Length)
        {
            Array.Resize(ref this.path, this.path.Length * 2);
        }

        int level = this.pathLength++;
        this.path[2 * level] = ++this.disjunctions;
        this.path[(2 * level) + 1] = 0;
        EcmaRegexNode first = this.Alternative();
        EcmaRegexNode result = first;
        if (this.Peek() == '|')
        {
            List<EcmaRegexNode> alternatives = [first];
            while (this.Eat('|'))
            {
                this.path[(2 * level) + 1]++;
                alternatives.Add(this.Alternative());
            }

            result = new EcmaRegexNode(EcmaRegexNodeKind.Alt) { Subs = [.. alternatives] };
        }

        this.pathLength--;
        this.depth--;
        return result;
    }

    private EcmaRegexNode Alternative()
    {
        List<EcmaRegexNode>? terms = null;
        EcmaRegexNode? only = null;
        while (this.More() && this.Peek() != '|' && this.Peek() != ')')
        {
            EcmaRegexNode term = this.Term();
            if (only is null && terms is null)
            {
                only = term;
            }
            else
            {
                terms ??= [only!];
                terms.Add(term);
            }
        }

        if (terms is not null)
        {
            return new EcmaRegexNode(EcmaRegexNodeKind.Cat) { Subs = [.. terms] };
        }

        return only ?? new EcmaRegexNode(EcmaRegexNodeKind.Empty);
    }

    /// <summary>
    /// The node for one literal character. Inside a case-insensitive group that is the set of the characters
    /// equivalent to it.
    /// </summary>
    private EcmaRegexNode CharNode(int c)
    {
        if (this.ignoreCase)
        {
            int[] set = EcmaRegexUnicode.FoldClosure([c, c], this.Unicode);
            if (set.Length != 2 || set[0] != set[1])
            {
                return SetNode(set);
            }
        }

        return new EcmaRegexNode(EcmaRegexNodeKind.Char) { CodePoint = c };
    }

    /// <summary>The set that also holds, inside a case-insensitive group, every character equivalent to a member.</summary>
    private int[] Folded(int[] set) => this.ignoreCase ? EcmaRegexUnicode.FoldClosure(set, this.Unicode) : set;

    private EcmaRegexNode Term()
    {
        this.work.Spend();
        int c = this.Peek();
        int at = this.i;
        bool quantifiable = true;
        EcmaRegexNode atom;
        switch (c)
        {
            case '^':
                this.i++;
                atom = new EcmaRegexNode(this.multiline ? EcmaRegexNodeKind.LineStart : EcmaRegexNodeKind.Bol);
                quantifiable = false;
                break;
            case '$':
                this.i++;
                atom = new EcmaRegexNode(this.multiline ? EcmaRegexNodeKind.LineEnd : EcmaRegexNodeKind.Eol);
                quantifiable = false;
                break;
            case '(':
                atom = this.GroupOrLook();

                // Annex B lets a quantifier follow a lookahead. The u flag does not, and neither lets one follow a
                // lookbehind.
                quantifiable = atom.Kind != EcmaRegexNodeKind.Look || (!this.Unicode && !atom.Behind);
                break;
            case '.':
                this.i++;
                atom = SetNode(this.dotAll ? this.Any : this.complements.Dot);
                break;
            case '[':
                atom = this.CharacterClass();
                break;
            case '\\':
                atom = this.AtomEscape();
                quantifiable = atom.Kind != EcmaRegexNodeKind.WordBoundary && atom.Kind != EcmaRegexNodeKind.NotWordBoundary;
                break;
            case '*':
            case '+':
            case '?':
                throw this.Error("Nothing to repeat");
            case '{':
                if (this.Unicode || this.LooksLikeQuantifier())
                {
                    throw this.Error(this.Unicode ? "Lone quantifier bracket" : "Nothing to repeat");
                }

                this.i++;
                atom = this.CharNode('{');
                break;
            case ']':
            case '}':
                if (this.Unicode)
                {
                    throw this.Error("Lone quantifier bracket");
                }

                this.i++;
                atom = this.CharNode(c);
                break;
            default:
                this.i++;
                atom = this.CharNode(c);
                break;
        }

        EcmaRegexNode? repeat = this.Quantifier();
        if (repeat is null)
        {
            return atom;
        }

        if (!quantifiable)
        {
            this.i = at;
            throw this.Error("Nothing to repeat");
        }

        repeat.Subs = [atom];
        return repeat;
    }

    /// <summary>Whether the text at the current '{' reads as <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>.</summary>
    private bool LooksLikeQuantifier()
    {
        int k = this.i + 1;
        int start = k;
        while (k < this.p.Length && IsDigit(this.p[k]))
        {
            k++;
        }

        if (k == start || k >= this.p.Length)
        {
            return false;
        }

        if (this.p[k] == '}')
        {
            return true;
        }

        if (this.p[k] != ',')
        {
            return false;
        }

        k++;
        while (k < this.p.Length && IsDigit(this.p[k]))
        {
            k++;
        }

        return k < this.p.Length && this.p[k] == '}';
    }

    /// <summary>Compares two runs of decimal digits as numbers, whatever their lengths.</summary>
    private static int CompareDigits(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        while (a.Length > 0 && a[0] == '0')
        {
            a = a.Slice(1);
        }

        while (b.Length > 0 && b[0] == '0')
        {
            b = b.Slice(1);
        }

        if (a.Length != b.Length)
        {
            return a.Length.CompareTo(b.Length);
        }

        for (int k = 0; k < a.Length; k++)
        {
            if (a[k] != b[k])
            {
                return a[k].CompareTo(b[k]);
            }
        }

        return 0;
    }

    /// <summary>Reads a run of decimal digits, which saturates at <see cref="MaxCount"/>.</summary>
    private int Number()
    {
        int start = this.i;
        long v = 0;
        while (this.i < this.p.Length && IsDigit(this.p[this.i]))
        {
            v = Math.Min((v * 10) + (this.p[this.i] - '0'), MaxCount);
            this.i++;
        }

        if (start == this.i)
        {
            throw this.Error("Expected a number");
        }

        return (int)v;
    }

    /// <summary>Reads a quantifier as a Repeat with no child yet, or returns null when there is none.</summary>
    private EcmaRegexNode? Quantifier()
    {
        int c = this.Peek();
        EcmaRegexNode repeat = new(EcmaRegexNodeKind.Repeat);
        if (c == '*')
        {
            this.i++;
            repeat.Max = EcmaRegexNode.Unbounded;
        }
        else if (c == '+')
        {
            this.i++;
            repeat.Min = 1;
            repeat.Max = EcmaRegexNode.Unbounded;
        }
        else if (c == '?')
        {
            this.i++;
            repeat.Max = 1;
        }
        else if (c == '{' && this.LooksLikeQuantifier())
        {
            this.i++;
            int minStart = this.i;
            repeat.Min = this.Number();
            int minEnd = this.i;
            repeat.Max = repeat.Min;
            bool outOfOrder = false;
            if (this.Eat(','))
            {
                if (this.Peek() == '}')
                {
                    repeat.Max = EcmaRegexNode.Unbounded;
                }
                else
                {
                    // The counts are compared as the numbers written, which can be past what a count is kept as:
                    // a{2147483648,2147483647} is out of order though both are kept as the greatest count.
                    int maxStart = this.i;
                    repeat.Max = this.Number();
                    outOfOrder = CompareDigits(this.p.AsSpan(maxStart, this.i - maxStart), this.p.AsSpan(minStart, minEnd - minStart)) < 0;
                }
            }

            if (!this.Eat('}'))
            {
                throw this.Error("Incomplete quantifier");
            }

            if (outOfOrder)
            {
                throw this.Error("Numbers out of order in {} quantifier");
            }
        }
        else
        {
            return null;
        }

        repeat.Lazy = this.Eat('?');
        return repeat;
    }

    /// <summary>Reads a group or a lookaround.</summary>
    private EcmaRegexNode GroupOrLook()
    {
        this.i++;
        if (!this.Eat('?'))
        {
            return this.Capture(null);
        }

        if (this.Eat(':'))
        {
            EcmaRegexNode group = new(EcmaRegexNodeKind.Group) { Subs = [this.Disjunction()] };
            this.Close();
            return group;
        }

        int c = this.Peek();
        if (c == 'i' || c == 'm' || c == 's' || c == '-')
        {
            return this.ModifierGroup();
        }

        if (this.Eat('=') || this.Eat('!'))
        {
            EcmaRegexNode look = new(EcmaRegexNodeKind.Look) { Negative = this.p[this.i - 1] == '!' };
            look.Subs = [this.Disjunction()];
            this.Close();
            return look;
        }

        if (this.Eat('<'))
        {
            if (this.Eat('=') || this.Eat('!'))
            {
                EcmaRegexNode look = new(EcmaRegexNodeKind.Look) { Behind = true, Negative = this.p[this.i - 1] == '!' };
                look.Subs = [this.Disjunction()];
                this.Close();
                return look;
            }

            string name = this.GroupName();

            // Two groups may share a name only when they are in separate alternatives of one disjunction, so that
            // no match can go through both.
            if (this.names.TryGetValue(name, out List<int[]>? others))
            {
                this.work.Spend((long)others.Count * (this.pathLength + 1));
                foreach (int[] other in others)
                {
                    if (!this.SeparateAlternatives(other))
                    {
                        throw this.Error("Duplicate group name '" + name + "'");
                    }
                }
            }

            return this.Capture(name);
        }

        throw this.Error("Invalid group");
    }

    /// <summary>
    /// Whether a named group (its number, then the alternatives that enclose it) and the current position lie in
    /// different alternatives of some disjunction.
    /// </summary>
    private bool SeparateAlternatives(int[] other)
    {
        for (int k = 0; (2 * k) + 2 < other.Length && k < this.pathLength && other[(2 * k) + 1] == this.path[2 * k]; k++)
        {
            if (other[(2 * k) + 2] != this.path[(2 * k) + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a group that turns modifiers on or off for its body, such as <c>(?i:...)</c> or <c>(?s-i:...)</c>.
    /// The position is after the "(?".
    /// </summary>
    private EcmaRegexNode ModifierGroup()
    {
        bool savedIgnoreCase = this.ignoreCase;
        bool savedMultiline = this.multiline;
        bool savedDotAll = this.dotAll;
        int seen = 0;
        bool removing = false;
        while (!this.Eat(':'))
        {
            int c = this.Peek();
            if (c == '-')
            {
                if (removing)
                {
                    throw this.Error("Invalid group modifiers");
                }

                this.i++;
                removing = true;
                continue;
            }

            int flag = c == 'i' ? 1 : c == 'm' ? 2 : c == 's' ? 4 : 0;
            if (flag == 0)
            {
                throw this.Error("Invalid group modifiers");
            }

            if ((seen & flag) != 0)
            {
                throw this.Error("Repeated group modifier");
            }

            this.i++;
            seen |= flag;
            if (flag == 1)
            {
                this.ignoreCase = !removing;
            }
            else if (flag == 2)
            {
                this.multiline = !removing;
            }
            else
            {
                this.dotAll = !removing;
            }
        }

        if (seen == 0)
        {
            this.i--;
            throw this.Error("Invalid group modifiers");
        }

        EcmaRegexNode group = new(EcmaRegexNodeKind.Group) { Subs = [this.Disjunction()] };
        this.Close();
        this.ignoreCase = savedIgnoreCase;
        this.multiline = savedMultiline;
        this.dotAll = savedDotAll;
        return group;
    }

    private EcmaRegexNode Capture(string? name)
    {
        EcmaRegexNode group = new(EcmaRegexNodeKind.Group) { Group = ++this.groups };
        if (name is not null)
        {
            this.work.Spend(this.pathLength);
            int[] named = new int[1 + (2 * this.pathLength)];
            named[0] = group.Group;
            Array.Copy(this.path, 0, named, 1, 2 * this.pathLength);
            if (!this.names.TryGetValue(name, out List<int[]>? list))
            {
                this.names[name] = list = [];
            }

            list.Add(named);
        }

        group.Subs = [this.Disjunction()];
        this.Close();
        return group;
    }

    private void Close()
    {
        if (!this.Eat(')'))
        {
            throw this.Error("Unterminated group");
        }
    }

    /// <summary>
    /// Reads a group name up to and including its '>'. A character of the name may be written as a Unicode escape.
    /// </summary>
    private string GroupName()
    {
        StringBuilder name = new();
        while (!this.Eat('>'))
        {
            if (!this.More())
            {
                throw this.Error("Invalid group name");
            }

            int c = this.p[this.i++];
            if (c == '\\')
            {
                if (!this.Eat('u'))
                {
                    throw this.Error("Invalid group name");
                }

                c = this.UnicodeEscape(braces: true, pairs: true);
                if (c < 0)
                {
                    throw this.Error("Invalid group name");
                }
            }
            else if (!this.Unicode && c >= 0xD800 && c <= 0xDBFF && this.More() && this.p[this.i] >= 0xDC00 && this.p[this.i] <= 0xDFFF)
            {
                // A name is made of code points whatever the grammar.
                c = char.ConvertToUtf32((char)c, (char)this.p[this.i++]);
            }

            if (name.Length == 0 ? !EcmaRegexUnicode.IsNameStart(c) : !EcmaRegexUnicode.IsNamePart(c))
            {
                throw this.Error("Invalid group name");
            }

            if (c > 0xFFFF)
            {
                name.Append(char.ConvertFromUtf32(c));
            }
            else
            {
                name.Append((char)c);
            }
        }

        if (name.Length == 0)
        {
            throw this.Error("Invalid group name");
        }

        return name.ToString();
    }

    /// <summary>Reads an escape outside a class.</summary>
    private EcmaRegexNode AtomEscape()
    {
        this.i++;
        if (!this.More())
        {
            throw this.Error("\\ at end of pattern");
        }

        int c = this.Peek();
        if (c == 'b' || c == 'B')
        {
            this.i++;

            // With the u flag grammar a case-insensitive word boundary counts two more characters as word
            // characters.
            return new EcmaRegexNode(c == 'b' ? EcmaRegexNodeKind.WordBoundary : EcmaRegexNodeKind.NotWordBoundary)
            {
                Fold = this.Unicode && this.ignoreCase,
            };
        }

        if (c == 'k')
        {
            this.i++;

            // Annex B reads \k as the letter k in a pattern with no named group.
            if (!this.Unicode && !this.hasNames)
            {
                return this.CharNode('k');
            }

            if (!this.Eat('<'))
            {
                throw this.Error("Invalid named reference");
            }

            EcmaRegexNode reference = new(EcmaRegexNodeKind.Backref) { Name = this.GroupName(), Fold = this.ignoreCase };
            this.namedRefs.Add(reference);
            this.HasBackreferences = true;
            return reference;
        }

        if (c >= '1' && c <= '9')
        {
            int start = this.i;
            int n = this.Number();
            if (n <= this.GroupCount)
            {
                this.HasBackreferences = true;
                return new EcmaRegexNode(EcmaRegexNodeKind.Backref) { Groups = [n], Fold = this.ignoreCase };
            }

            if (this.Unicode)
            {
                this.i = start;
                throw this.Error("Reference to a group that does not exist");
            }

            // Annex B reads it as an octal escape, or as the digit itself.
            this.i = start;
            return this.CharNode(this.LegacyOctal());
        }

        int[]? set = this.ClassEscape(c);
        if (set is not null)
        {
            if (this.escapeStrings is not null)
            {
                // A property of strings (the v flag): the class of its code points and its sequences.
                return this.ClassNode(this.StringsValue(this.Folded(set), this.escapeStrings), negated: false);
            }

            return SetNode(this.Folded(set));
        }

        return this.CharNode(this.CharacterEscape(inClass: false));
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
            v = (v * 8) + (this.Peek() - '0');
            this.i++;
            k++;
        }

        if (k == 0)
        {
            return this.p[this.i++];
        }

        return v;
    }

    /// <summary>
    /// Reads a class escape such as <c>\d</c> or <c>\p{...}</c> whose letter is <paramref name="c"/>, as a set.
    /// Returns null, reading nothing, when <paramref name="c"/> does not start one.
    /// </summary>
    private int[]? ClassEscape(int c)
    {
        this.escapeStrings = null;
        bool foldWord = this.Unicode && this.ignoreCase;
        switch (c)
        {
            case 'd':
                this.i++;
                return Digit;
            case 'D':
                this.i++;
                return this.complements.NotDigit;
            case 'w':
                this.i++;
                return foldWord ? FoldWord : Word;
            case 'W':
                this.i++;
                return foldWord ? this.complements.NotFoldWord : this.complements.NotWord;
            case 's':
                this.i++;
                return Space;
            case 'S':
                this.i++;
                return this.complements.NotSpace;
            case 'p':
            case 'P':
            {
                // Without the u flag \p is the letter p.
                if (!this.Unicode)
                {
                    return null;
                }

                this.i++;
                if (!this.Eat('{'))
                {
                    throw this.Error("Invalid property name");
                }

                int start = this.i;
                while (this.More() && this.Peek() != '}')
                {
                    this.i++;
                }

                char[] expression = new char[this.i - start];
                for (int k = 0; k < expression.Length; k++)
                {
                    // A property name is ASCII, so a character beyond it only has to stay a character that is in no name.
                    expression[k] = this.p[start + k] > 0xFFFF ? (char)0xFFFF : (char)this.p[start + k];
                }

                if (!this.Eat('}'))
                {
                    throw this.Error("Invalid property name");
                }

                int[]? set = EcmaRegexUnicode.Property(expression, out _);
                this.work.Spend(8);
                if (set is null && this.unicodeSets && EcmaRegexUnicode.StringProperty(expression, out int[] codePoints, out int[][] strings))
                {
                    // A property of strings, which the v flag alone names, and which has no complement.
                    if (c == 'P')
                    {
                        this.i = start;
                        throw this.Error("Invalid property name");
                    }

                    this.escapeStrings = strings;
                    return codePoints;
                }

                if (set is null)
                {
                    this.i = start;
                    throw this.Error("Invalid property name");
                }

                // With the v flag the complement is of the case-folded set; with the u flag the fold comes after.
                return c == 'P' ? EcmaRegexUnicode.Complement(this.unicodeSets ? this.Folded(set) : set, this.max) : set;
            }

            default:
                return null;
        }
    }

    /// <summary>Reads exactly <paramref name="digits"/> hexadecimal digits. Returns -1, reading nothing, when they are not there.</summary>
    private int Hex(int digits)
    {
        if (this.i + digits > this.p.Length)
        {
            return -1;
        }

        int v = 0;
        for (int k = 0; k < digits; k++)
        {
            int d = HexValue(this.p[this.i + k]);
            if (d < 0)
            {
                return -1;
            }

            v = (v * 16) + d;
        }

        this.i += digits;
        return v;
    }

    /// <summary>Reads the escape after a backslash, in or out of a class, and returns the character it names.</summary>
    private int CharacterEscape(bool inClass)
    {
        int c = this.p[this.i++];
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
                    this.i++;
                    return l % 32;
                }

                if (!this.Unicode && inClass && (IsDigit(l) || l == '_'))
                {
                    this.i++;
                    return l % 32;
                }

                if (this.Unicode)
                {
                    throw this.Error("Invalid control escape");
                }

                // Annex B reads a \c that is not a control escape as a backslash followed by the letter c.
                this.i--;
                return '\\';
            }

            case '0':
                if (this.More() && IsDigit(this.Peek()))
                {
                    if (this.Unicode)
                    {
                        throw this.Error("Invalid decimal escape");
                    }

                    this.i--;
                    return this.LegacyOctal();
                }

                return 0;
            case 'x':
            {
                int h = this.Hex(2);
                if (h < 0)
                {
                    if (this.Unicode)
                    {
                        throw this.Error("Invalid \\x escape");
                    }

                    return 'x';
                }

                return h;
            }

            case 'u':
            {
                int u = this.UnicodeEscape(braces: this.Unicode, pairs: this.Unicode);
                if (u < 0)
                {
                    if (this.Unicode)
                    {
                        throw this.Error("Invalid \\u escape");
                    }

                    return 'u';
                }

                return u;
            }

            default:
                break;
        }

        if (c >= '1' && c <= '9' && !this.Unicode && inClass)
        {
            this.i--;
            return this.LegacyOctal();
        }

        // Identity escapes. The u flag allows only the syntax characters and '/', and '-' in a class. Annex B
        // allows any character, but k in a pattern with a named group: there \k is the start of a named reference
        // wherever it stands, and a class has none.
        if ((this.Unicode && !(c < 128 && "^$\\.*+?()[]{}|/".IndexOf((char)c) >= 0) && !(inClass && c == '-'))
            || (c == 'k' && this.hasNames))
        {
            this.i--;
            throw this.Error("Invalid escape");
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

                this.i++;
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

        if (pairs && u >= 0xD800 && u <= 0xDBFF && this.i + 6 <= this.p.Length && this.p[this.i] == '\\' && this.p[this.i + 1] == 'u')
        {
            int save = this.i;
            this.i += 2;
            int low = this.Hex(4);
            if (low >= 0xDC00 && low <= 0xDFFF)
            {
                return char.ConvertToUtf32((char)u, (char)low);
            }

            this.i = save;
        }

        return u;
    }

    private void AddPair(int lo, int hi)
    {
        if (this.pairCount + 2 > this.pairs.Length)
        {
            Array.Resize(ref this.pairs, this.pairs.Length * 2);
        }

        this.pairs[this.pairCount++] = lo;
        this.pairs[this.pairCount++] = hi;
    }

    private void AddClassAtom(int c, int[]? set)
    {
        this.work.Spend(1 + ((set?.Length ?? 0) / 32));
        if (set is null)
        {
            this.AddPair(c, c);
            return;
        }

        for (int k = 0; k < set.Length; k += 2)
        {
            this.AddPair(set[k], set[k + 1]);
        }
    }

    /// <summary>Reads a character class from its '[' to its ']'.</summary>
    private EcmaRegexNode CharacterClass()
    {
        if (this.unicodeSets)
        {
            return this.ClassSetExpression();
        }

        this.i++;
        bool negated = this.Eat('^');
        this.pairCount = 0;
        while (true)
        {
            if (!this.More())
            {
                throw this.Error("Unterminated character class");
            }

            if (this.Eat(']'))
            {
                break;
            }

            int first = this.ClassAtom();
            int[]? firstSet = this.atomSet;
            if (this.Peek() == '-' && this.i + 1 < this.p.Length && this.p[this.i + 1] != ']')
            {
                int dash = this.i;
                this.i++;
                int second = this.ClassAtom();
                int[]? secondSet = this.atomSet;
                if (firstSet is not null || secondSet is not null)
                {
                    if (this.Unicode)
                    {
                        this.i = dash;
                        throw this.Error("Invalid character class range");
                    }

                    // Annex B reads the '-' as itself when a class escape is at either end.
                    this.AddClassAtom(first, firstSet);
                    this.AddPair('-', '-');
                    this.AddClassAtom(second, secondSet);
                    continue;
                }

                if (second < first)
                {
                    this.i = dash;
                    throw this.Error("Range out of order in character class");
                }

                this.AddPair(first, second);
                continue;
            }

            this.AddClassAtom(first, firstSet);
        }

        // The ranges are sorted and gone through for what is equivalent to them, and the operations on sets count
        // the steps of that.
        this.work.Spend(1 + (this.pairCount / 32));
        int[] all = EcmaRegexUnicode.SetOf(this.pairs, this.pairCount);

        // Inside a case-insensitive group a character matches the class when it is equivalent to a member, and a
        // negated class excludes exactly those characters.
        int[] set = this.Folded(all);
        return SetNode(negated ? EcmaRegexUnicode.Complement(set, this.max) : set);
    }

    /// <summary>
    /// Reads a class of the v flag's grammar from its '[' to its ']': a union of characters, ranges, nested classes,
    /// class escapes, strings and properties of strings; or the intersection (&amp;&amp;) or the difference (--) of
    /// such operands, which may not be mixed with a union or with each other without a nested class around them.
    /// With the i flag each operand is case folded before the operations, so that the complement of a class is
    /// of the folded set. A negated class may not hold anything that may match a string.
    /// </summary>
    private EcmaRegexNode ClassSetExpression()
    {
        this.i++;
        bool negated = this.Eat('^');
        ClassSetValue value = this.ClassSetContents();
        if (!this.Eat(']'))
        {
            throw this.Error("Unterminated character class");
        }

        if (negated && value.MayContainStrings)
        {
            throw this.Error("Negated character class may contain strings");
        }

        return this.ClassNode(value, negated);
    }

    /// <summary>Reads the contents of a class of the v flag's grammar, up to its ']'.</summary>
    private ClassSetValue ClassSetContents()
    {
        if (this.Peek() == ']')
        {
            return new ClassSetValue([], [], false);
        }

        ClassSetValue value = this.ClassSetElement(out bool range);
        int op = this.SetOperator();
        if (op != 0)
        {
            // An intersection or a difference: operands only, with the one operator between every two.
            if (range)
            {
                throw this.Error("Invalid set operation in character class");
            }

            while (this.SetOperator() == op)
            {
                this.i += 2;
                if (this.Peek() == ']' || !this.More())
                {
                    throw this.Error("Invalid set operation in character class");
                }

                ClassSetValue operand = this.ClassSetElement(out bool operandRange);
                if (operandRange)
                {
                    throw this.Error("Invalid set operation in character class");
                }

                value = op == '&' ? Intersect(value, operand) : this.Subtract(value, operand);
            }

            if (this.Peek() != ']')
            {
                throw this.Error("Invalid set operation in character class");
            }

            return value;
        }

        // A union.
        while (this.More() && this.Peek() != ']')
        {
            if (this.SetOperator() != 0)
            {
                throw this.Error("Invalid set operation in character class");
            }

            value = Union(value, this.ClassSetElement(out _));
        }

        return value;
    }

    /// <summary>The set operator at the position ('&amp;' for &amp;&amp;, '-' for --), or 0.</summary>
    private int SetOperator()
    {
        if (this.i + 1 < this.p.Length && this.p[this.i] == this.p[this.i + 1] && (this.p[this.i] == '&' || this.p[this.i] == '-'))
        {
            return this.p[this.i];
        }

        return 0;
    }

    /// <summary>Reads one element of a class of the v flag's grammar: an operand, or a range of two characters.</summary>
    private ClassSetValue ClassSetElement(out bool range)
    {
        range = false;
        int c = this.Peek();
        if (c == '[')
        {
            // A nested class: its strings count as strings whether or not there are any. It is a level of nesting,
            // as a group is: the reader goes a level deeper for it.
            DeepStack.Ensure();
            if (++this.depth > MaxNesting)
            {
                throw new RegexPatternTooLargeException(
                    "Its groups and classes nest more than " + MaxNesting.ToString(System.Globalization.CultureInfo.InvariantCulture) + " deep.");
            }

            int start = this.i;
            this.i++;
            bool negated = this.Eat('^');
            ClassSetValue inner = this.ClassSetContents();
            if (!this.Eat(']'))
            {
                throw this.Error("Unterminated character class");
            }

            this.depth--;

            if (negated)
            {
                if (inner.MayContainStrings)
                {
                    this.i = start;
                    throw this.Error("Negated character class may contain strings");
                }

                return new ClassSetValue(EcmaRegexUnicode.Complement(inner.Set, this.max), [], false);
            }

            return inner;
        }

        if (c == '\\')
        {
            if (this.i + 1 >= this.p.Length)
            {
                throw this.Error("\\ at end of pattern");
            }

            int e = this.p[this.i + 1];
            if (e == 'q')
            {
                this.i += 2;
                return this.ClassStringDisjunction();
            }

            this.i++;
            int[]? set = this.ClassEscape(e);
            if (set is not null)
            {
                if (this.escapeStrings is not null)
                {
                    return this.StringsValue(this.Folded(set), this.escapeStrings);
                }

                // A class escape is folded as one operand; \P and the complements are already folded (see ClassEscape).
                return new ClassSetValue(e is 'P' or 'D' or 'W' or 'S' ? set : this.Folded(set), [], false);
            }

            this.i--;
        }

        int first = this.ClassSetCharacter();
        if (this.Peek() == '-' && this.SetOperator() == 0)
        {
            int dash = this.i;
            this.i++;
            if (this.Peek() == ']' || this.Peek() == '[' || (this.Peek() == '\\' && this.i + 1 < this.p.Length && "qdDwWsSpP".IndexOf((char)this.p[this.i + 1]) >= 0 && this.p[this.i + 1] < 128))
            {
                this.i = dash;
                throw this.Error("Invalid character class range");
            }

            int second = this.ClassSetCharacter();
            if (second < first)
            {
                this.i = dash;
                throw this.Error("Range out of order in character class");
            }

            range = true;
            return new ClassSetValue(this.Folded([first, second]), [], false);
        }

        return new ClassSetValue(this.Folded([first, first]), [], false);
    }

    /// <summary>
    /// Reads one character of a class of the v flag's grammar: a character that is not a syntax character and does
    /// not begin a reserved double punctuator, or an escape, which may be of a reserved punctuator.
    /// </summary>
    private int ClassSetCharacter()
    {
        if (!this.More())
        {
            throw this.Error("Unterminated character class");
        }

        int c = this.p[this.i];
        if (c == '\\')
        {
            if (this.i + 1 >= this.p.Length)
            {
                throw this.Error("\\ at end of pattern");
            }

            int e = this.p[this.i + 1];
            if (e == 'b')
            {
                this.i += 2;
                return '\b';
            }

            if (e < 128 && "&-!#%,:;<=>@`~".IndexOf((char)e) >= 0)
            {
                this.i += 2;
                return e;
            }

            this.i++;
            return this.CharacterEscape(inClass: true);
        }

        if (c < 128 && "()[]{}/-\\|".IndexOf((char)c) >= 0)
        {
            throw this.Error("Invalid character in character class");
        }

        if (this.i + 1 < this.p.Length && this.p[this.i + 1] == c && c < 128 && "&!#$%*+,.:;<=>?@^`~".IndexOf((char)c) >= 0)
        {
            throw this.Error("Invalid set operation in character class");
        }

        this.i++;
        return c;
    }

    /// <summary>Reads the strings of <c>\q{...}</c> after its <c>\q</c>: a string of one character is a character of the class.</summary>
    private ClassSetValue ClassStringDisjunction()
    {
        if (!this.Eat('{'))
        {
            throw this.Error("Invalid escape");
        }

        List<int> current = [];
        List<int[]> strings = [];
        int[] set = [];
        bool mayContainStrings = false;
        while (true)
        {
            if (!this.More())
            {
                throw this.Error("Unterminated character class");
            }

            int c = this.Peek();
            if (c == '|' || c == '}')
            {
                this.i++;
                if (current.Count == 1)
                {
                    set = EcmaRegexUnicode.Union(set, this.Folded([current[0], current[0]]));
                }
                else
                {
                    mayContainStrings = true;
                    strings.Add(this.CanonicalString(current));
                }

                current.Clear();
                if (c == '}')
                {
                    break;
                }

                continue;
            }

            current.Add(this.ClassSetCharacter());
        }

        return new ClassSetValue(set, strings, mayContainStrings);
    }

    /// <summary>The value of a property of strings: its code points, and its sequences (case folded in a case-insensitive group).</summary>
    private ClassSetValue StringsValue(int[] set, int[][] strings)
    {
        List<int[]> list = new(strings.Length);
        foreach (int[] sequence in strings)
        {
            list.Add(this.CanonicalString(sequence));
        }

        this.work.Spend(1 + (strings.Length / 32));
        return new ClassSetValue(set, list, true);
    }

    /// <summary>A string as its code points, each canonicalized in a case-insensitive group so that strings are compared as ECMA-262 compares them.</summary>
    private int[] CanonicalString(IReadOnlyList<int> codePoints)
    {
        int[] result = new int[codePoints.Count];
        for (int k = 0; k < result.Length; k++)
        {
            result[k] = this.ignoreCase ? EcmaRegexUnicode.Canonicalize(codePoints[k], unicode: true) : codePoints[k];
        }

        return result;
    }

    private static ClassSetValue Union(ClassSetValue a, ClassSetValue b)
    {
        List<int[]> strings = new(a.Strings);
        foreach (int[] str in b.Strings)
        {
            if (!HasString(strings, str))
            {
                strings.Add(str);
            }
        }

        return new ClassSetValue(EcmaRegexUnicode.Union(a.Set, b.Set), strings, a.MayContainStrings || b.MayContainStrings);
    }

    private static ClassSetValue Intersect(ClassSetValue a, ClassSetValue b)
    {
        List<int[]> strings = [];
        foreach (int[] str in a.Strings)
        {
            if (HasString(b.Strings, str))
            {
                strings.Add(str);
            }
        }

        return new ClassSetValue(EcmaRegexUnicode.Intersect(a.Set, b.Set), strings, a.MayContainStrings && b.MayContainStrings);
    }

    private ClassSetValue Subtract(ClassSetValue a, ClassSetValue b)
    {
        List<int[]> strings = [];
        foreach (int[] str in a.Strings)
        {
            if (!HasString(b.Strings, str))
            {
                strings.Add(str);
            }
        }

        return new ClassSetValue(EcmaRegexUnicode.Difference(a.Set, b.Set, this.max), strings, a.MayContainStrings);
    }

    private static bool HasString(List<int[]> strings, int[] str)
    {
        foreach (int[] s in strings)
        {
            if (s.AsSpan().SequenceEqual(str))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The node of a class of the v flag's grammar: its set, or, where it holds strings, the alternatives of its
    /// strings longest first and then its set, as ECMA-262 orders them, in a group.
    /// </summary>
    private EcmaRegexNode ClassNode(ClassSetValue value, bool negated)
    {
        if (negated)
        {
            return SetNode(EcmaRegexUnicode.Complement(value.Set, this.max));
        }

        if (value.Strings.Count == 0)
        {
            return SetNode(value.Set);
        }

        List<int[]> ordered = new(value.Strings);
        ordered.Sort((x, y) => y.Length.CompareTo(x.Length));
        List<EcmaRegexNode> alternatives = new(ordered.Count + 1);
        foreach (int[] str in ordered)
        {
            if (str.Length == 0)
            {
                alternatives.Add(new EcmaRegexNode(EcmaRegexNodeKind.Empty));
                continue;
            }

            EcmaRegexNode[] chars = new EcmaRegexNode[str.Length];
            for (int k = 0; k < str.Length; k++)
            {
                chars[k] = this.CharNode(str[k]);
            }

            alternatives.Add(new EcmaRegexNode(EcmaRegexNodeKind.Cat) { Subs = chars });
        }

        if (value.Set.Length != 0)
        {
            alternatives.Add(SetNode(value.Set));
        }

        this.work.Spend(1 + (alternatives.Count / 8));
        return new EcmaRegexNode(EcmaRegexNodeKind.Group) { Subs = [new EcmaRegexNode(EcmaRegexNodeKind.Alt) { Subs = [.. alternatives] }] };
    }

    /// <summary>The value of a class of the v flag's grammar: its code points, its strings (each of a length other than one, the empty string among them), and whether its syntax may hold a string.</summary>
    private readonly record struct ClassSetValue(int[] Set, List<int[]> Strings, bool MayContainStrings);

    /// <summary>Reads one member of a class. Returns its character, or sets <see cref="atomSet"/> for a class escape.</summary>
    private int ClassAtom()
    {
        this.atomSet = null;
        int c = this.p[this.i++];
        if (c != '\\')
        {
            return c;
        }

        if (!this.More())
        {
            throw this.Error("\\ at end of pattern");
        }

        int e = this.Peek();
        if (e == 'b')
        {
            this.i++;
            return '\b';
        }

        this.atomSet = this.ClassEscape(e);
        if (this.atomSet is not null)
        {
            return 0;
        }

        return this.CharacterEscape(inClass: true);
    }

    /// <summary>The complements of the sets ECMA-262 names, among the characters up to the last one of a grammar.</summary>
    private sealed class Complements(int max)
    {
        public int[] Any { get; } = [0, max];

        public int[] Dot { get; } = EcmaRegexUnicode.Complement(LineTerminators, max);

        public int[] NotDigit { get; } = EcmaRegexUnicode.Complement(Digit, max);

        public int[] NotWord { get; } = EcmaRegexUnicode.Complement(Word, max);

        public int[] NotFoldWord { get; } = EcmaRegexUnicode.Complement(FoldWord, max);

        public int[] NotSpace { get; } = EcmaRegexUnicode.Complement(Space, max);
    }
}
