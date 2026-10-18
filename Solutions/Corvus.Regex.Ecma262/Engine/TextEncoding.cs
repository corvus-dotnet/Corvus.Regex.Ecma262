// <copyright file="TextEncoding.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of units.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself units.go, and what the Go source
// takes from the unicode/utf8 package), generalised over the two encodings the library matches natively.

using System;
#if NET
using System.Buffers;
#endif
using System.Runtime.CompilerServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Reads the text a match runs over, in one of the two encodings. The matcher is specialised over a struct that
/// implements this interface, so no virtual call and no branch on the encoding is made in its loop.
/// </summary>
/// <typeparam name="TUnit">The unit of the text: <see cref="byte"/> for UTF-8, <see cref="char"/> for UTF-16.</typeparam>
/// <remarks>
/// A position is an offset into the text in its own units. ECMA-262 matches a pattern read without the u flag a
/// UTF-16 code unit at a time, so each encoding can also read the text that way. Over UTF-16 that is the natural
/// reading. Over UTF-8 a character of four bytes is two code units, a high surrogate and a low one, and the position
/// between them is written as the offset of the character's third byte, which is a continuation byte and so no
/// position at all when reading by code point.
/// </remarks>
internal interface ITextEncoding<TUnit>
    where TUnit : unmanaged, IEquatable<TUnit>
{
    /// <summary>The unit at a position, as a number.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns>The unit.</returns>
    int Unit(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>The code point at a position and its width in units. Bytes that are not UTF-8 decode to U+FFFD one at a time, and a lone surrogate of UTF-16 to itself.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    int Decode(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The code point that ends at a position and its width in units.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    int DecodeBefore(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The UTF-16 code unit at a position and its width in units, for a pattern matched by code unit.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code unit.</returns>
    int UnitAt(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The UTF-16 code unit that ends at a position and its width in units.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code unit.</returns>
    int UnitBefore(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>Whether the character at a position is one of the four ECMA-262 line terminators.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsLineTerminatorAt(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>Whether the character that ends at a position is one of the four ECMA-262 line terminators.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsLineTerminatorBefore(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>Whether the literal run of an instruction is at a position, and its width in units when it is.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <param name="program">The program.</param>
    /// <param name="lit">The number of the literal.</param>
    /// <param name="width">The width.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool LiteralAt(ReadOnlySpan<TUnit> input, int pos, Program program, int lit, out int width);

    /// <summary>The position of the first occurrence of the program's prefix at or after a position, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="program">The program.</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfPrefix(ReadOnlySpan<TUnit> input, int from, Program program);

    /// <summary>The position of the first occurrence of the program's required literal at or after a position, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="program">The program.</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfRequired(ReadOnlySpan<TUnit> input, int from, Program program);

    /// <summary>The next position after a position that a match can start at: the next unit that is not a continuation byte.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns>The position.</returns>
    int NextStart(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>
    /// The position one UTF-16 code unit on from a position, which is how ECMA-262 advances past an empty match
    /// without the u flag (AdvanceStringIndex). Over UTF-8 that can be the position between the halves of a
    /// character of four bytes.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns>The position.</returns>
    int AdvanceUnit(ReadOnlySpan<TUnit> input, int pos);

#if NET
    /// <summary>The ASCII members of a set, in the unit of the text, for a vectorised search.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The members.</returns>
    SearchValues<TUnit> Members(CharSet set);

    /// <summary>The ASCII characters that are not members of a set, in the unit of the text, for a vectorised search.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The non-members.</returns>
    SearchValues<TUnit> NonMembers(CharSet set);

    /// <summary>A character below 128 as a unit of the text.</summary>
    /// <param name="c">The character.</param>
    /// <returns>The unit.</returns>
    TUnit AsciiUnit(int c);

    /// <summary>
    /// The first position at or after a position where a character of the set can be: an exact member when the
    /// set is small, otherwise an ASCII member or any character that is not ASCII. The caller decodes and checks.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="set">The set.</param>
    /// <param name="small">Whether the set is small (see <see cref="CharSet.IsSmall"/>).</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfCandidate(ReadOnlySpan<TUnit> input, int from, CharSet set, bool small);

    /// <summary>
    /// The first position at or after a position that a match may start at. Over UTF-8 a candidate the scan lands
    /// on can be a continuation byte: a stray one is a character (U+FFFD) and so a start; one inside a character is
    /// not, unless it is the third byte of a four-byte character and the text is matched by code unit.
    /// </summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The position.</returns>
    int AlignStart(ReadOnlySpan<TUnit> input, int pos, bool units);

    /// <summary>The first position at or after a position of the program's case-insensitive ASCII literal, or -1 (see <see cref="Program.CasePrefixUtf16"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="program">The program.</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfCasePrefix(ReadOnlySpan<TUnit> input, int from, Program program);

    /// <summary>The last position in a range from which a way can go on, by the quick's allowed characters, or -1 (only when the quick is <see cref="Quick.AsciiOnly"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The start of the range.</param>
    /// <param name="to">The end of the range, exclusive.</param>
    /// <param name="quick">The quick.</param>
    /// <returns>The position, or -1.</returns>
    int LastAllowed(ReadOnlySpan<TUnit> input, int from, int to, ref Quick quick);

    /// <summary>The first position at or after a position of the literal after the program's leading loop, or -1 (see <see cref="Program.AfterLoopUtf16"/>).</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="program">The program.</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfAfterLoop(ReadOnlySpan<TUnit> input, int from, Program program);
#endif

    /// <summary>The required literal's distance from the start of a match over this encoding (see <see cref="Program.RequiredOffset"/>).</summary>
    /// <param name="program">The program.</param>
    /// <returns>The distance, or -1.</returns>
    int RequiredOffset(Program program);

    /// <summary>The length of the program's prefix in units of this encoding.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The length.</returns>
    int PrefixLength(Program program);

    /// <summary>The position a number of characters (code points, or code units when <paramref name="units"/>) before a position, or -1 when fewer lie between it and <paramref name="floor"/>.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="count">The number of characters.</param>
    /// <param name="floor">The position the walk may not go before.</param>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns>The position, or -1.</returns>
    int WalkBack(ReadOnlySpan<TUnit> input, int from, int count, int floor, bool units);
}

/// <summary>Reads UTF-8 text.</summary>
internal readonly struct Utf8Encoding : ITextEncoding<byte>
{
    /// <summary>What a byte that is not UTF-8 decodes to, one byte at a time.</summary>
    public const int RuneError = 0xFFFD;

    /// <summary>Whether a unit below 128 is an ECMA-262 word character.</summary>
    /// <param name="b">The unit.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWordUnit(int b)
    {
        return (uint)(b - '0') <= 9 || (uint)((b | 0x20) - 'a') <= 25 || b == '_';
    }

    /// <summary>The code point at the start of a span and its width in bytes: U+FFFD and 1 for bytes that are not UTF-8, and U+FFFD and 0 for no bytes.</summary>
    /// <param name="p">The span.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    public static int DecodeRune(ReadOnlySpan<byte> p, out int width)
    {
        if (p.Length == 0)
        {
            width = 0;
            return RuneError;
        }

        int p0 = p[0];
        if (p0 < 0x80)
        {
            width = 1;
            return p0;
        }

        if (p0 >= 0xC2 && p0 <= 0xDF)
        {
            if (p.Length >= 2 && IsContinuation(p[1]))
            {
                width = 2;
                return ((p0 & 0x1F) << 6) | (p[1] & 0x3F);
            }
        }
        else if (p0 >= 0xE0 && p0 <= 0xEF)
        {
            if (p.Length >= 3 && IsContinuation(p[2]))
            {
                int lo = p0 == 0xE0 ? 0xA0 : 0x80;
                int hi = p0 == 0xED ? 0x9F : 0xBF;
                if (p[1] >= lo && p[1] <= hi)
                {
                    width = 3;
                    return ((p0 & 0x0F) << 12) | ((p[1] & 0x3F) << 6) | (p[2] & 0x3F);
                }
            }
        }
        else if (p0 >= 0xF0 && p0 <= 0xF4)
        {
            if (p.Length >= 4 && IsContinuation(p[2]) && IsContinuation(p[3]))
            {
                int lo = p0 == 0xF0 ? 0x90 : 0x80;
                int hi = p0 == 0xF4 ? 0x8F : 0xBF;
                if (p[1] >= lo && p[1] <= hi)
                {
                    width = 4;
                    return ((p0 & 0x07) << 18) | ((p[1] & 0x3F) << 12) | ((p[2] & 0x3F) << 6) | (p[3] & 0x3F);
                }
            }
        }

        width = 1;
        return RuneError;
    }

    /// <summary>The code point at the end of a span and its width in bytes.</summary>
    /// <param name="p">The span.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    public static int DecodeLastRune(ReadOnlySpan<byte> p, out int width)
    {
        int end = p.Length;
        if (end == 0)
        {
            width = 0;
            return RuneError;
        }

        int last = p[end - 1];
        if (last < 0x80)
        {
            width = 1;
            return last;
        }

        // A character is four bytes at most, so its first byte is one of the last four.
        int lim = Math.Max(end - 4, 0);
        int start = end - 1;
        while (start > lim)
        {
            start--;
            if (!IsContinuation(p[start]))
            {
                break;
            }
        }

        int r = DecodeRune(p.Slice(start, end - start), out int size);
        if (start + size != end)
        {
            width = 1;
            return RuneError;
        }

        width = size;
        return r;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unit(ReadOnlySpan<byte> input, int pos) => input[pos];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ReadOnlySpan<byte> input, int pos, out int width) => DecodeRune(input.Slice(pos), out width);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBefore(ReadOnlySpan<byte> input, int pos, out int width) => DecodeLastRune(input.Slice(0, pos), out width);

    /// <inheritdoc/>
    public int UnitAt(ReadOnlySpan<byte> input, int pos, out int width)
    {
        int c = input[pos];
        if (c >= 0xF0)
        {
            int r = DecodeRune(input.Slice(pos), out int w);
            if (w == 4)
            {
                width = 2;
                return HighSurrogate(r);
            }

            width = 1;
            return RuneError;
        }

        if (c >= 0x80 && c < 0xC0 && pos >= 2 && input[pos - 2] >= 0xF0)
        {
            // Between the halves of the character that starts two bytes back.
            int r = DecodeRune(input.Slice(pos - 2), out int w);
            if (w == 4)
            {
                width = 2;
                return LowSurrogate(r);
            }
        }

        return DecodeRune(input.Slice(pos), out width);
    }

    /// <inheritdoc/>
    public int UnitBefore(ReadOnlySpan<byte> input, int pos, out int width)
    {
        int r = DecodeLastRune(input.Slice(0, pos), out int w);
        if (w == 4)
        {
            width = 2;
            return LowSurrogate(r);
        }

        if (w == 1 && r == RuneError && pos >= 2 && input.Length - pos >= 2 && input[pos - 2] >= 0xF0)
        {
            // Between the halves of the character that starts two bytes back.
            int r2 = DecodeRune(input.Slice(pos - 2), out int w2);
            if (w2 == 4)
            {
                width = 2;
                return HighSurrogate(r2);
            }
        }

        width = w;
        return r;
    }

    /// <inheritdoc/>
    public bool IsLineTerminatorAt(ReadOnlySpan<byte> input, int pos)
    {
        int c = input[pos];
        if (c == '\n' || c == '\r')
        {
            return true;
        }

        // U+2028 and U+2029 are E2 80 A8 and E2 80 A9.
        return c == 0xE2 && pos + 2 < input.Length && input[pos + 1] == 0x80 && (input[pos + 2] == 0xA8 || input[pos + 2] == 0xA9);
    }

    /// <inheritdoc/>
    public bool IsLineTerminatorBefore(ReadOnlySpan<byte> input, int pos)
    {
        int c = input[pos - 1];
        if (c == '\n' || c == '\r')
        {
            return true;
        }

        return (c == 0xA8 || c == 0xA9) && pos >= 3 && input[pos - 3] == 0xE2 && input[pos - 2] == 0x80;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool LiteralAt(ReadOnlySpan<byte> input, int pos, Program program, int lit, out int width)
    {
        byte[] bytes = program.Utf8Literals[lit];
        width = bytes.Length;
        return input.Length - pos >= bytes.Length && input.Slice(pos, bytes.Length).SequenceEqual(bytes);
    }

    /// <inheritdoc/>
    public int IndexOfPrefix(ReadOnlySpan<byte> input, int from, Program program)
    {
        byte[] prefix = program.Utf8Prefix;
        if (prefix[0] < 0x80)
        {
            int i = input.Slice(from).IndexOf(prefix);
            return i < 0 ? -1 : from + i;
        }

        // The first and last bytes of a prefix beyond ASCII are the first bytes of a script's characters, which
        // every character of the script shares, so the span search would compare at nearly every character. The
        // last byte of the first character tells characters apart; it is searched for, and the prefix compared
        // from the character's start.
        DecodeRune(prefix, out int firstWidth);
        byte trail = prefix[firstWidth - 1];
        int at = from + firstWidth - 1;
        while (at < input.Length)
        {
            int j = input.Slice(at).IndexOf(trail);
            if (j < 0)
            {
                return -1;
            }

            int start = at + j - firstWidth + 1;
            if (start >= from && input.Length - start >= prefix.Length && input.Slice(start, prefix.Length).SequenceEqual(prefix))
            {
                return start;
            }

            at += j + 1;
        }

        return -1;
    }

    /// <inheritdoc/>
    public int IndexOfRequired(ReadOnlySpan<byte> input, int from, Program program)
    {
        if (from > input.Length)
        {
            return -1;
        }

        int i = input.Slice(from).IndexOf(program.RequiredUtf8);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    public int NextStart(ReadOnlySpan<byte> input, int pos)
    {
        pos++;
        while (pos < input.Length && IsContinuation(input[pos]))
        {
            pos++;
        }

        return pos;
    }

    /// <inheritdoc/>
    public int AdvanceUnit(ReadOnlySpan<byte> input, int pos)
    {
        int c = input[pos];
        if (c >= 0xF0)
        {
            DecodeRune(input.Slice(pos), out int w);
            return w == 4 ? pos + 2 : pos + 1;
        }

        if (c >= 0x80 && c < 0xC0 && pos >= 2 && input[pos - 2] >= 0xF0)
        {
            DecodeRune(input.Slice(pos - 2), out int w);
            if (w == 4)
            {
                return pos + 2;
            }
        }

        DecodeRune(input.Slice(pos), out int width);
        return pos + width;
    }

#if NET
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SearchValues<byte> Members(CharSet set) => set.MemberBytes;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SearchValues<byte> NonMembers(CharSet set) => set.NonMemberBytes;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte AsciiUnit(int c) => (byte)c;

    /// <inheritdoc/>
    public int IndexOfCandidate(ReadOnlySpan<byte> input, int from, CharSet set, bool small)
    {
        if (!small)
        {
            int i = set.HasNonAscii ? input.Slice(from).IndexOfAnyExcept(set.NonMemberBytes) : input.Slice(from).IndexOfAny(set.MemberBytes);
            return i < 0 ? -1 : from + i;
        }

        if (!set.ScansByTrailByte)
        {
            int i = input.Slice(from).IndexOfAny(set.LeadBytes);
            return i < 0 ? -1 : from + i;
        }

        // The last byte of a member's encoding tells members of one script apart where the first byte does not
        // (every Cyrillic letter starts with D0 or D1). The character that ends at the byte found is decoded
        // backwards; a byte that ends no character is a character of its own.
        int at = from;
        while (true)
        {
            int i = input.Slice(at).IndexOfAny(set.TrailBytes);
            if (i < 0)
            {
                return -1;
            }

            int end = at + i + 1;
            DecodeLastRune(input.Slice(0, end), out int w);
            if (w == 1 && IsContinuation(input[end - 1]))
            {
                // A continuation byte that ends no character: inside a character (a middle byte that happens to be
                // a trail value), in which case the search goes on after that character, or stray, in which case it
                // is a character of its own.
                if (this.AlignStart(input, end - 1, false) != end - 1)
                {
                    // Inside a character: its own last byte may be the trail looked for, so go on from the next byte.
                    at = end;
                    continue;
                }
            }

            int start = end - w;
            if (start >= from)
            {
                return start;
            }

            // The character that ends here began before the range.
            at = end;
        }
    }

    /// <inheritdoc/>
    public int AlignStart(ReadOnlySpan<byte> input, int pos, bool units)
    {
        if (!IsContinuation(input[pos]))
        {
            return pos;
        }

        int lead = pos - 1;
        while (lead >= 0 && pos - lead < 4 && IsContinuation(input[lead]))
        {
            lead--;
        }

        if (lead < 0 || IsContinuation(input[lead]))
        {
            // No lead byte within reach: a stray continuation byte, which is a character of its own.
            return pos;
        }

        DecodeRune(input.Slice(lead), out int w);
        if (lead + w <= pos)
        {
            return pos;
        }

        if (units && w == 4)
        {
            return pos <= lead + 2 ? lead + 2 : lead + 4;
        }

        return lead + w;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int LastAllowed(ReadOnlySpan<byte> input, int from, int to, ref Quick quick)
    {
        int i = input.Slice(from, to - from).LastIndexOfAny(quick.AllowedBytes);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    public int IndexOfAfterLoop(ReadOnlySpan<byte> input, int from, Program program)
    {
        if (from > input.Length)
        {
            return -1;
        }

        int i = input.Slice(from).IndexOf(program.AfterLoopUtf8);
        return i < 0 ? -1 : from + i;
    }
#endif

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int RequiredOffset(Program program) => program.RequiredOffsetUtf8;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PrefixLength(Program program) => program.Utf8Prefix.Length;

    /// <inheritdoc/>
    public int WalkBack(ReadOnlySpan<byte> input, int from, int count, int floor, bool units)
    {
        int at = from;
        for (int k = 0; k < count; k++)
        {
            if (at <= floor)
            {
                return -1;
            }

            int w;
            if (units)
            {
                this.UnitBefore(input, at, out w);
            }
            else
            {
                DecodeLastRune(input.Slice(0, at), out w);
            }

            at -= w;
        }

        return at < floor ? -1 : at;
    }

#if NET
    /// <inheritdoc/>
    public int IndexOfCasePrefix(ReadOnlySpan<byte> input, int from, Program program)
    {
        byte[] lower = program.CasePrefixUtf8;
        int n = lower.Length;
        byte[][] pairs = program.CasePrefixPairs;
        while (true)
        {
            if (input.Length - from < n)
            {
                return -1;
            }

            // The first two characters in each of their cases, as two-byte needles the span search vectorises;
            // the nearest occurrence is the candidate. Each needle after the first is looked for only up to the
            // best so far, so a rare needle never scans the whole text at every step.
            ReadOnlySpan<byte> window = input.Slice(from, input.Length - from - n + 2);
            int best = -1;
            foreach (byte[] pair in pairs)
            {
                ReadOnlySpan<byte> scope = best < 0 ? window : window.Slice(0, best + 1);
                int j = scope.IndexOf(pair);
                if (j >= 0 && (best < 0 || j < best))
                {
                    best = j;
                    if (j == 0)
                    {
                        break;
                    }
                }
            }

            if (best < 0)
            {
                return -1;
            }

            from += best;
            if (System.Text.Ascii.EqualsIgnoreCase(input.Slice(from, n), lower))
            {
                return from;
            }

            from++;
        }
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsContinuation(int b) => (b & 0xC0) == 0x80;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HighSurrogate(int c) => 0xD800 + ((c - 0x10000) >> 10);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LowSurrogate(int c) => 0xDC00 + ((c - 0x10000) & 0x3FF);
}

/// <summary>Reads UTF-16 text.</summary>
internal readonly struct Utf16Encoding : ITextEncoding<char>
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unit(ReadOnlySpan<char> input, int pos) => input[pos];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ReadOnlySpan<char> input, int pos, out int width)
    {
        char c = input[pos];
        if (char.IsHighSurrogate(c) && pos + 1 < input.Length && char.IsLowSurrogate(input[pos + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(c, input[pos + 1]);
        }

        width = 1;
        return c;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBefore(ReadOnlySpan<char> input, int pos, out int width)
    {
        char c = input[pos - 1];
        if (char.IsLowSurrogate(c) && pos >= 2 && char.IsHighSurrogate(input[pos - 2]))
        {
            width = 2;
            return char.ConvertToUtf32(input[pos - 2], c);
        }

        width = 1;
        return c;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UnitAt(ReadOnlySpan<char> input, int pos, out int width)
    {
        width = 1;
        return input[pos];
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UnitBefore(ReadOnlySpan<char> input, int pos, out int width)
    {
        width = 1;
        return input[pos - 1];
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLineTerminatorAt(ReadOnlySpan<char> input, int pos)
    {
        char c = input[pos];
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLineTerminatorBefore(ReadOnlySpan<char> input, int pos)
    {
        char c = input[pos - 1];
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool LiteralAt(ReadOnlySpan<char> input, int pos, Program program, int lit, out int width)
    {
        string chars = program.Utf16Literals[lit];
        width = chars.Length;
        return input.Length - pos >= chars.Length && input.Slice(pos, chars.Length).SequenceEqual(chars.AsSpan());
    }

    /// <inheritdoc/>
    public int IndexOfPrefix(ReadOnlySpan<char> input, int from, Program program)
    {
        int i = input.Slice(from).IndexOf(program.Utf16Prefix.AsSpan());
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    public int IndexOfRequired(ReadOnlySpan<char> input, int from, Program program)
    {
        if (from > input.Length)
        {
            return -1;
        }

        int i = input.Slice(from).IndexOf(program.RequiredUtf16.AsSpan());
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextStart(ReadOnlySpan<char> input, int pos) => pos + 1;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AdvanceUnit(ReadOnlySpan<char> input, int pos) => pos + 1;

#if NET
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SearchValues<char> Members(CharSet set) => set.MemberChars;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SearchValues<char> NonMembers(CharSet set) => set.NonMemberChars;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char AsciiUnit(int c) => (char)c;

    /// <inheritdoc/>
    public int IndexOfCandidate(ReadOnlySpan<char> input, int from, CharSet set, bool small)
    {
        int i = small
            ? input.Slice(from).IndexOfAny(set.AllChars)
            : set.HasNonAscii ? input.Slice(from).IndexOfAnyExcept(set.NonMemberChars) : input.Slice(from).IndexOfAny(set.MemberChars);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AlignStart(ReadOnlySpan<char> input, int pos, bool units)
    {
        // By code point, the low half of a surrogate pair is inside a character.
        if (!units && pos > 0 && char.IsLowSurrogate(input[pos]) && char.IsHighSurrogate(input[pos - 1]))
        {
            return pos + 1;
        }

        return pos;
    }

    /// <inheritdoc/>
    public int IndexOfCasePrefix(ReadOnlySpan<char> input, int from, Program program)
    {
        int i = input.Slice(from).IndexOf(program.CasePrefixUtf16.AsSpan(), StringComparison.OrdinalIgnoreCase);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int LastAllowed(ReadOnlySpan<char> input, int from, int to, ref Quick quick)
    {
        int i = input.Slice(from, to - from).LastIndexOfAny(quick.AllowedChars);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    public int IndexOfAfterLoop(ReadOnlySpan<char> input, int from, Program program)
    {
        if (from > input.Length)
        {
            return -1;
        }

        int i = input.Slice(from).IndexOf(program.AfterLoopUtf16.AsSpan());
        return i < 0 ? -1 : from + i;
    }
#endif

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int RequiredOffset(Program program) => program.RequiredOffset;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PrefixLength(Program program) => program.Utf16Prefix.Length;

    /// <inheritdoc/>
    public int WalkBack(ReadOnlySpan<char> input, int from, int count, int floor, bool units)
    {
        int at = from;
        for (int k = 0; k < count; k++)
        {
            if (at <= floor)
            {
                return -1;
            }

            if (units)
            {
                at--;
            }
            else
            {
                this.DecodeBefore(input, at, out int w);
                at -= w;
            }
        }

        return at < floor ? -1 : at;
    }
}
