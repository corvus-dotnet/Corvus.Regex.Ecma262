// <copyright file="PrefilterTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The scans that find where a match can start, and the loops that keep no choice, give the same answers as the
/// plain search: the faults the data set found while they were written, and the shapes each is for.
/// </summary>
[TestClass]
public class PrefilterTests
{
    [TestMethod]
    [DataRow(@"\ude00", "u", "😀,", false, "the low half of a pair is inside a character by code point")]
    [DataRow(@"\ude00", "", "😀,", true, "and a code unit without the u flag")]
    [DataRow(@"\ud83d", "", "😀", true, "the high half is read at the first byte over UTF-8")]
    [DataRow(@"[\ude00\ude01]x", "", "😀x", true, "a small set of low surrogates")]
    [DataRow(@"[^a]x", "u", "😀x", true, "a large set lands on a lead byte, not inside the character")]
    [DataRow(@"[a-z]shing", "u", "the fishing trip", true, "a required literal after a fixed ASCII term")]
    [DataRow(@"[a-z]shing", "u", "the shing trip", false, "the start the literal allows must match")]
    [DataRow(@"[a-z]shing", "u", "xshing shing", true, "")]
    [DataRow(@"[a-q][^u-z]{13}x", "u", "abcdefghijklmnx", true, "")]
    [DataRow(@"twain", "iu", "Mark TWAIN wrote", true, "a case-insensitive ASCII prefix")]
    [DataRow(@"twain", "iu", "Mark TWAIM wrote", false, "")]
    [DataRow(@"sawyer", "iu", "tom ſawyer", true, "the long s folds to s under the u flag, so this is not a plain case prefix")]
    [DataRow(@"sawyer", "i", "tom ſawyer", false, "and without it the long s stays itself")]
    [DataRow(@"[\w.+-]+@[\w.-]+", "u", "mail someone.else@example.com now", true, "an atomic loop before a literal")]
    [DataRow(@"[\w.+-]+@[\w.-]+", "u", "mail someone@@example now", false, "the loop cannot take the @, so no run precedes the second")]
    [DataRow(@"\w+\s+Holmes", "u", "Mr Sherlock  Holmes", true, "")]
    [DataRow(@"a*ab", "u", "aaab", true, "a loop that is not atomic still gives back")]
    [DataRow(@"a*", "u", "", true, "")]
    [DataRow(@"[^\n]*x", "u", "éé€ x", true, "a set that holds everything beyond ASCII")]
    [DataRow(@"[^\n]*x", "u", "é\n x", true, "")]
    [DataRow(@"^[^\n]*x$", "u", "é\n x", false, "")]
    [DataRow(@"Шерлок Холмс|Джон Уотсон", "u", "где Джон Уотсон", true, "a small first set beyond ASCII")]
    [DataRow(@"夏洛克|约翰", "u", "是约翰吗", true, "")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "x ab@c.d", true, "a literal after a loop: the run before the literal is walked back")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "x @c.d", false, "and a loop of at least one needs a run")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "a@b a@c.d", true, "a failed occurrence moves on to the next")]
    [DataRow(@"[\w]*://x", "u", "see ://x", true, "a loop of at least zero needs none")]
    [DataRow(@"Sherlock", "iu", "a ſherlock", true, "the long s folds to s under the u flag: an extra beyond the ordinal comparison")]
    [DataRow(@"Sherlock", "iu", "a SHERLOC\u212a", true, "and the Kelvin sign to k")]
    [DataRow(@"Sherlock", "iu", "a SHERLOCK", true, "")]
    [DataRow(@"Sherlock", "iu", "a ſherloc", false, "")]
    [DataRow(@"Sherlock", "i", "a ſherlock", false, "without the u flag the long s is itself")]
    [DataRow(@"\w+\s+Holmes", "u", "Sherlock  Holmes", true, "a failed start skips to the end of its run")]
    [DataRow(@"\w+\s+Holmes", "u", "SherlockHolmes Holmes", true, "")]
    [DataRow(@"\w+\s+Holmes", "u", "Sherlock Holme", false, "")]
    [DataRow(@"(\w+)\s+Holmes", "u", "x Sherlock Holmes", true, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "ééé\u00e9xy", true, "a required literal after a fixed prefix beyond ASCII, which only UTF-16 can offset")]
    [DataRow(@"[^u-z]{3}xy", "u", "éxy", false, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "\ud83d\ude00\ud83d\ude00\ud83d\ude00xy", true, "by code point a supplementary character is one member, two units")]
    [DataRow(@"[^u-z]{3}xy", "", "\ud83d\ude00\ud83d\ude00\ud83d\ude00xy", true, "and by code unit it is two members")]
    [DataRow(@"[^u-z]{3}xy", "", "a\ud83d\ude00xy", true, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "a\ud83d\ude00xy", false, "")]
    [DataRow(@"(.*)$", "u", "\nabd", true, "a start the loop took nothing at begins no run to skip")]
    [DataRow(@"[a-zA-Z]+ing", "u", "sing singing s", true, "a leading loop that keeps its choices still lets a failed start skip its run")]
    [DataRow(@"[a-zA-Z]+ing", "u", "sin sinn", false, "")]
    [DataRow(@"\b\w+n\b", "u", "the lion sleeps", true, "an assertion before the leading loop stops the skipping")]
    [DataRow(@"\b\w+n\b", "u", "the lions sleep", false, "")]
    [DataRow(@"\B\w+.{0,2}\S$", "u", "ab c", true, "a start inside the word holds \\B where the word's start does not")]
    [DataRow(@"\bt\w+", "u", "at tab", true, "a leading word boundary passes over the rest of a word")]
    [DataRow(@"\bt\w+", "u", "at", false, "")]
    [DataRow(@"\b\w+", "u", "ſherlock", true, "the long s is no word character, so a word starts after it")]
    [DataRow(@"the", "u", "In the end", true, "a literal alone")]
    [DataRow(@"the", "yu", "In the end", false, "sticky: not at the start")]
    [DataRow(@"\p{Lu}", "u", "abcDef", true, "a class alone")]
    [DataRow(@"\p{Lu}", "yu", "abcDef", false, "")]
    [DataRow(@"\p{Lu}", "yu", "Def", true, "")]
    [DataRow(@"[\ud800-\udbff]", "", "a\ud83d\ude00", true, "a class alone of surrogate units, found over UTF-8 at the first byte")]
    [DataRow(@"[\udc00-\udfff]", "", "a\ud83d\ude00", true, "and at the third")]
    [DataRow(@"[\udc00-\udfff]", "u", "a\ud83d\ude00", false, "by code point the pair is one character")]
    [DataRow(@"the", "iu", "In THE end", true, "a case-insensitive literal alone")]
    [DataRow(@"Sherlock", "iu", "and ſHERLOCK", true, "a sequence of classes alone, with a fold beyond ASCII")]
    [DataRow(@"Sherlock", "iyu", "and ſHERLOCK", false, "")]
    [DataRow(@"Шерлок", "iu", "mr шЕРЛОК", true, "a sequence of classes beyond ASCII")]
    [DataRow(@"Шерлок", "iyu", "mr шЕРЛОК", false, "")]
    [DataRow(@"Шерлок", "iyu", "шЕРЛОК", true, "")]
    [DataRow(@"Holmes", "iu", "mr holmeſ", true, "the long s is in the fold class of s under the u flag")]
    [DataRow(@"Holmes", "i", "mr holmeſ", false, "and not without it")]
    [DataRow(@"Holmes", "iu", "mr HOLMES!", true, "")]
    [DataRow(@"Holmes", "iyu", "mr HOLMES!", false, "")]
    [DataRow(@"Kelvin", "iu", "a \u212aELVIN", true, "the Kelvin sign is in the fold class of k")]
    [DataRow(@"Kelvin", "iu", "a \u212aELVI", false, "")]
    [DataRow(@"Kelvin", "iu", "ſ \u212aELVIN", true, "an extra that is not a start")]
    [DataRow(@"Шs", "iu", "x шſ", true, "a sequence beyond ASCII whose class of s holds the long s, which the ordinal comparison does not")]
    [DataRow(@"Шs", "iu", "x шS", true, "")]
    [DataRow(@"Шk", "iu", "x ш\u212a", true, "")]
    [DataRow(@"Шерлок", "iu", "mr шЕРЛО", false, "")]
    [DataRow(@"[ab]x[cd]", "u", "zzaxd", true, "")]
    [DataRow(@".*", "u", "", true, "a loop alone matches the empty text")]
    [DataRow(@"[a-z]+", "u", "123", false, "")]
    [DataRow(@"[a-z]+", "yu", "1abc", false, "")]
    [DataRow(@"[a-z]+", "yu", "abc1", true, "")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "u", "poor Huckleberry Finn", true, "an alternation of literals alone")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "yu", "poor Huckleberry Finn", false, "")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "u", "poor Huck", false, "")]
    [DataRow(@"Шерлок Холмс|Джон Уотсон", "u", "где Джон Уотсон", true, "")]
    [DataRow(@"the", "iyu", "In THE end", false, "")]
    [DataRow(@"[\ud83d\ude00-\ud83d\ude4f]", "u", "\ud83d\ude00,", true, "a trail-byte scan can land on a middle byte of the member it is looking for")]
    [DataRow(@"[\u0428\u0448]x", "u", "\u0448x", true, "")]
    [DataRow(@"[\u0428\u0448]x", "u", "\u0418x", false, "")]
    [DataRow(@"\S{2}\ud801\udc00$", "u", "\ud801\udc00\ud801\udc00\ud801\udc00", true, "the literal's first occurrence has too few characters before it; a later one does")]
    [DataRow(@"\S{2}\ud801\udc00$", "u", "\ud801\udc00\ud801\udc00", false, "")]
    [DataRow(@"[^u-z]{13}x", "u", "\ud83d\ude00abcdefghijklx", true, "a required literal reached by walking back over characters of two units")]
    [DataRow(@"[a-z]*x", "u", "1ax", true, "")]
    [DataRow("\u03b8x", "iu", "a\u03f4x", true, "a character of four forms (theta) anchors nothing: the search falls back")]
    [DataRow("\u03b8x", "iu", "a\u03d1X", true, "")]
    [DataRow("\u03b8x", "iu", "a\u03f4y", false, "")]
    [DataRow("\u03b8 ab", "iu", "\u03d1 AB", true, "and with other characters to anchor on it is searched for by them")]
    [DataRow("\u03b8 ab", "iu", "\u03b8 ac", false, "")]
    [DataRow("\ud801\udc00a", "iu", "x\ud801\udc28A", true, "a supplementary character's cases differ in the low surrogate, two forms of two units")]
    [DataRow("\ud801\udc00a", "iu", "x\ud801\udc29A", false, "")]
    [DataRow("\u0428x", "iu", "\u0408x", false, "over UTF-8 the forms of a letter differ in both bytes (D0 A8, D1 88): a byte of each is not a form (D0 88)")]
    [DataRow("\u0428x", "iu", "\u0468x", false, "nor D1 A8")]
    [DataRow("\u0428x", "iu", "\u0448X", true, "")]
    [DataRow(@"[ab][cd]", "u", "zzbd", true, "a small class of single characters is a character of several forms")]
    [DataRow(@"[ab][cd]", "u", "zzbb", false, "")]
    [DataRow(@"ss", "iu", "x\u017fS", true, "three forms at every position (s, S, the long s): anchors of three values over UTF-16, extras over UTF-8")]
    [DataRow(@"ss", "iu", "x\u017f\u017f", true, "")]
    [DataRow(@"ss", "iu", "xs\u017fx", true, "")]
    [DataRow(@"ss", "iu", "x\u017fxs", false, "")]
    [DataRow(@"k\u0428", "iu", "x\u212a\u0448", true, "the Kelvin sign before a letter beyond ASCII: an extra whose window is wider than the literal over UTF-8")]
    [DataRow(@"k\u0428", "iu", "x\u212a\u0418", false, "")]
    [DataRow(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", "u", "Today is 11/18/2019", true, "a word boundary in the automaton")]
    [DataRow(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", "u", "Today is 111/18/2019", false, "")]
    [DataRow(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", "u", "11/18/2019x", false, "")]
    [DataRow(@"\B^([a-z]+\.)*.$", "u", "$", true, "a start anchor after a boundary at the start is still at the start")]
    [DataRow(@"\b(?:\b|\B)^", "u", "aaa", true, "")]
    [DataRow(@"(\B){3}^a?", "u", "}", true, "")]
    [DataRow(@"\Bx\b", "u", "ax b", true, "a boundary after a unit")]
    [DataRow(@"\Bx\b", "u", "axb", false, "")]
    [DataRow(@"x\b", "u", "x", true, "a boundary at the end of the text")]
    [DataRow(@"x\B", "u", "x", false, "")]
    [DataRow(@"\w{10,}", "u", "the consectetur word", true, "a leading loop of any least count skips its run")]
    [DataRow(@"\w{10,}", "u", "the consecte word", false, "")]
    [DataRow(@".{0,2}(Tom|Sawyer)", "u", "xxxTom", true, "a bounded loop then an alternation: each occurrence allows the starts the loop could reach it from")]
    [DataRow(@".{0,2}(Tom|Sawyer)", "yu", "xxTom", true, "")]
    [DataRow(@".{0,2}(Tom|Sawyer)", "yu", "xxxTom", false, "")]
    [DataRow(@"\d{2,4}(ab|cd)", "u", "1ab 12cd", true, "the first occurrence allows no start; the next does")]
    [DataRow(@"\d{2,4}(ab|cd)", "u", "1ab 1cd", false, "")]
    [DataRow(@"[a-z]{1,3}(ab|cd)", "u", "zzzzab", true, "")]
    [DataRow(@"[a-z]{1,3}(ab|cd)", "u", "ab", false, "the loop needs one")]
    [DataRow(@".{0,2}(tom|sawyer)", "iu", "xxTOM", true, "the alternation in its forms")]
    [DataRow(@".{0,2}(tom|sawyer)", "iu", "xx\u017fawyer", true, "a form the search leaves out (the long s)")]
    [DataRow(@".{0,2}(tom|sawyer)", "iyu", "xxx\u017fawyer", false, "")]
    [DataRow(@".{0,2}(tom|sawyer)", "iyu", "xx\u017fawyer", true, "")]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "u", "a fishing trip", true, "a required literal after a bounded repetition: so many to so many characters before it")]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "u", "a fishingtrip", false, "")]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "u", "x\u00a0swimming\u00a0y", true, "walked back over characters of more than one unit over UTF-8")]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "u", "x thirteenlettersing y", false, "")]
    [DataRow(@"a.{2,3}x", "u", "a12x", true, "")]
    [DataRow(@"a.{2,3}x", "u", "a1x", false, "")]
    [DataRow(@"a.{2,3}x", "u", "a1234x", false, "")]
    [DataRow(@"a.{2,3}x", "u", "a1x a123x", true, "the first occurrence allows no start that matches; a later one does")]
    [DataRow(@"a.{2,3}x", "u", "a\ud83d\ude00\ud83d\ude00x", true, "a character of two units is one")]
    [DataRow(@"a.{2,3}x", "", "a\ud83d\ude00x", true, "and two without the u flag, within the bound")]
    [DataRow(@"a.{2,3}x", "", "a\ud83d\ude00\ud83d\ude00x", false, "")]
    [DataRow(@"[a-zA-Z]+ing", "u", "the singing bird", true, "a literal after a loop whose set holds the literal's own characters: the run before the occurrence is walked back")]
    [DataRow(@"[a-zA-Z]+ing", "u", "ing sing", true, "")]
    [DataRow(@"[a-zA-Z]+ing", "u", "ing", false, "the loop needs one character")]
    [DataRow(@"[a-zA-Z]+ing", "yu", "xing", true, "")]
    [DataRow(@"[a-zA-Z]+ing", "yu", " xing", false, "")]
    [DataRow(@"\w+\s+Holmes", "u", "Mr Sherlock  Holmes!", true, "a literal after a run of loops over sets that share no character")]
    [DataRow(@"\w+\s+Holmes", "u", "Mr SherlockHolmes", false, "")]
    [DataRow(@"\w+\s+Holmes", "u", " Holmes", false, "the first loop needs a character")]
    [DataRow(@"\w+\s+Holmes", "u", "Mr\u00a0\u2003Holmes", true, "a loop over a set beyond ASCII is walked back by characters")]
    [DataRow(@"\w+\s+Holmes", "u", "\u00e9\u00a0Holmes", false, "the first loop's set is ASCII")]
    [DataRow(@"[^\s]+\s+Holmes", "u", "\u00e9\u00a0Holmes", true, "")]
    [DataRow(@"([A-Za-z]awyer|[A-Za-z]inn)\s", "u", "Tom Sawyer and Huck Finn ", true, "every alternative holds a literal at a known distance: the literals are searched for as one")]
    [DataRow(@"([A-Za-z]awyer|[A-Za-z]inn)\s", "u", "Tom Sawyer and Huck Finn", true, "")]
    [DataRow(@"([A-Za-z]awyer|[A-Za-z]inn)\s", "u", "Tom Sawyerand Huck Finn", false, "")]
    [DataRow(@"([A-Za-z]awyer|[A-Za-z]inn)\s", "u", "awyer inn ", false, "")]
    [DataRow(@"Tom.{10,25}river|river.{10,25}Tom", "u", "river to the Tom", false, "")]
    [DataRow(@"Tom.{10,25}river|river.{10,25}Tom", "u", "river and then Tom", true, "")]
    [DataRow(@"Tom.{10,25}river|river.{10,25}Tom", "u", "Tom went down to the river", true, "")]
    [DataRow(@"Sher[a-z]+|Hol[a-z]+", "iu", "mr HOLMES", true, "the alternatives' leading literals in their forms")]
    [DataRow(@"Sher[a-z]+|Hol[a-z]+", "iu", "mr HOL", false, "")]
    [DataRow(@"Sher[a-z]+|Hol[a-z]+", "iu", "\u017fherlock", true, "a form the search leaves out")]
    [DataRow(@"(ab|cd)x", "yu", "zabx", false, "sticky")]
    [DataRow(@"(ab|cd)x", "yu", "cdx", true, "")]
    [DataRow(@"\u5915\u6d1b|\u7ea6\u7ff0", "u", "x\u7ea6\u7ff0", true, "an alternation beyond ASCII keeps the needle")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "u", "the letters of the greek script run from alpha through to omega, with kappa", true, "ten alternatives share the fingerprint search's eight buckets")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "u", "kappa", true, "a text shorter than a vector is searched by the tail")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "u", "the letters of the greek script run from a through to omega, with kapp", false, "")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "u", "the letters of the greek script run from a through to omega, with kappa", true, "a match in the last positions of the text")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "iu", "THE LETTERS OF THE GREEK SCRIPT RUN FROM A THROUGH TO OMEGA, WITH IOTA", true, "the forms of the letters are in the fingerprints")]
    [DataRow(@"alpha|beta|gamma|delta|epsilon|zeta|eta|theta|iota|kappa", "iu", "THE LETTERS OF THE GREEK SCRIPT RUN FROM A THROUGH TO OMEGA, WITH IOT", false, "")]
    [DataRow(@"\u0428\u0435\u0440\u043b\u043e\u043a|\u0423\u043e\u0442\u0441\u043e\u043d|\u041b\u0435\u0441\u0442\u0440\u0435\u0439\u0434", "u", "\u0434\u043e\u043a\u0442\u043e\u0440 \u0423\u043e\u0442\u0441\u043e\u043d \u0438 \u0428\u0435\u0440\u043b\u043e\u043a \u0425\u043e\u043b\u043c\u0441 \u0432\u0441\u0442\u0440\u0435\u0442\u0438\u043b\u0438 \u041b\u0435\u0441\u0442\u0440\u0435\u0439\u0434\u0430", true, "Cyrillic literals: the fingerprints are the bytes that tell them apart, not the lead bytes")]
    [DataRow(@"\u0428\u0435\u0440\u043b\u043e\u043a|\u0423\u043e\u0442\u0441\u043e\u043d|\u041b\u0435\u0441\u0442\u0440\u0435\u0439\u0434", "iu", "\u0434\u043e\u043a\u0442\u043e\u0440 \u0443\u043e\u0442\u0441\u043e\u043d \u0438 \u0445\u043e\u043b\u043c\u0441", true, "")]
    [DataRow(@"\u0428\u0435\u0440\u043b\u043e\u043a|\u0423\u043e\u0442\u0441\u043e\u043d|\u041b\u0435\u0441\u0442\u0440\u0435\u0439\u0434", "u", "\u0434\u043e\u043a\u0442\u043e\u0440 \u0443\u043e\u0442\u0441\u043e\u043d \u0438 \u0445\u043e\u043b\u043c\u0441", false, "")]
    [DataRow(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", "iu", "221B BAKER STREET", true, "")]
    [DataRow(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", "iu", "221B BAKE STREET", false, "")]
    [DataRow(@"\w+\s+Holmes\s+\w+", "u", "x Holmes  y", true, "")]
    [DataRow(@"\w+\s+Holmes\s+\w+", "u", "x Holmes y Holmes", true, "")]
    [DataRow(@"\w+\s+Holmes\s+\w+", "u", "x Holmes", false, "")]
    [DataRow(@"\b\w+n\b", "u", "n", false, "an assertion before the loops is left to the matcher")]
    [DataRow(@"\b\w+n\b", "u", "nn", true, "")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "iu", "a hUCKLEBERRY finn", true, "an alternation of literals in their forms, searched for as one")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "iu", "a hUCKLEBERRY \u017fawyer", true, "")]
    [DataRow(@"Tom|Sawyer|Huckleberry|Finn", "iu", "a hUCKLEBERR", false, "")]
    [DataRow(@"Sherlock|Holmes|Watson", "iu", "mr HOLME\u017f", true, "the long s at the end of an alternative")]
    [DataRow(@"Sherlock|Holmes|Watson", "iyu", "HOLME\u017f", true, "")]
    [DataRow(@"Sherlock|Holmes|Watson", "iyu", "xHOLME\u017f", false, "")]
    public void TheScansAgreeWithThePlainSearchOverBothEncodings(string pattern, string flags, string text, bool expected, string why)
    {
        (Regex interpreted, Regex generated) = Engines.Both(pattern, flags);
        foreach ((Regex r, string engine) in new[] { (interpreted, "interpreted"), (generated, "generated") })
        {
            Assert.AreEqual(expected, r.IsMatch(text), $"UTF-16 {engine} {why}");
            Assert.AreEqual(expected, r.IsMatch(Encoding.UTF8.GetBytes(text)), $"UTF-8 {engine} {why}");
            Assert.AreEqual(expected, r.Match(text).Success, $"Match {engine} {why}");
        }
    }

    [TestMethod]
    public void WhatAnEnumerationLearnedOfOneTextDoesNotServeALaterMatchOnAnother()
    {
        // An enumeration carries the ranges it found clear of the long s and of the rare line terminators; a match
        // on its own, on the same thread, starts with nothing.
        (Regex namesInterpreted, Regex namesGenerated) = Engines.Both("Sher[a-z]+|Hol[a-z]+", "iu");
        (Regex restInterpreted, Regex restGenerated) = Engines.Both(".*", "gu");
        foreach ((Regex names, Regex rest) in new[] { (namesInterpreted, restInterpreted), (namesGenerated, restGenerated) })
        {
            Assert.AreEqual(4, names.Count("sherlock holmes sherlock holmes"));
            Assert.IsTrue(names.IsMatch("\u017fherlock"));
            Assert.IsTrue(names.Match("xx \u017fherlock").Success);
            Assert.AreEqual(2, rest.Count("ab cd ef gh"));
            Assert.AreEqual("ab", rest.Match("ab\u2028cd").Value);
            Assert.IsTrue(rest.TryMatchBounds(Encoding.UTF8.GetBytes("ab\u2028cd"), 0, false, out int from8, out int to8) && from8 == 0 && to8 == 2);
            int lines = 0;
            foreach (ValueMatch m in rest.EnumerateMatches("ab\u2029cd\r\nef"))
            {
                lines++;
            }

            Assert.AreEqual(7, lines);
        }
    }

    [TestMethod]
    public void TheCountOfMatchesIsTheSameOverBothEncodingsAndAsNetCountsThem()
    {
        string text = "Шерлок Холмс and Джон Уотсон met 夏洛克 at 10.0.0.1, then x@y.z and TWAIN twain Twain, ſherlock \u212aing. ababc aabc abab a1b2c3 t1 tab";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        foreach ((string pattern, string flags, string netPattern, System.Text.RegularExpressions.RegexOptions options) in new[]
        {
            (@"Шерлок Холмс|Джон Уотсон|夏洛克", "gu", @"Шерлок Холмс|Джон Уотсон|夏洛克", System.Text.RegularExpressions.RegexOptions.None),
            (@"twain", "giu", @"twain", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"шерлок|джон", "giu", @"шерлок|джон", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"ſherlock|\u212aing", "giu", @"[sſ]herlock|[k\u212a]ing", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"[\w.+-]+@[\w.-]+\.[\w.-]+", "gu", @"[a-zA-Z0-9_.+-]+@[a-zA-Z0-9_.-]+\.[a-zA-Z0-9_.-]+", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "gu", @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", System.Text.RegularExpressions.RegexOptions.None),
            (@"\w+", "gu", @"[a-zA-Z0-9_]+", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-z]+n", "gu", @"[a-z]+n", System.Text.RegularExpressions.RegexOptions.None),
            (@"\w+\s+and", "gu", @"[a-zA-Z0-9_]+\s+and", System.Text.RegularExpressions.RegexOptions.None),
            (@"[^u-z]{3}in", "gu", @"[^u-z]{3}in", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-zA-Z]+in", "gu", @"[a-zA-Z]+in", System.Text.RegularExpressions.RegexOptions.None),
            (@"\b\w+n\b", "gu", @"\b[a-zA-Z0-9_]+n\b", System.Text.RegularExpressions.RegexOptions.None),
            (@"n", "gu", @"n", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-z]", "gu", @"[a-z]", System.Text.RegularExpressions.RegexOptions.None),
            (@"and", "giu", @"and", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"[^\n]*", "gu", @"[^\n]*", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-z]+", "gu", @"[a-z]+", System.Text.RegularExpressions.RegexOptions.None),
            (@"and|met|at|x", "gu", @"and|met|at|x", System.Text.RegularExpressions.RegexOptions.None),
            (@"Шерлок Холмс|Джон Уотсон|夏洛克", "gu", @"Шерлок Холмс|Джон Уотсон|夏洛克", System.Text.RegularExpressions.RegexOptions.None),
            (@"TWAIN|twain", "gu", @"TWAIN|twain", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:ab|a){2,3}c", "gu", @"(?:ab|a){2,3}c", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:ab|a){1,2}?b", "gu", @"(?:ab|a){1,2}?b", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:[a-z]\d){2}", "gu", @"(?:[a-z]\d){2}", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:\d\.){3}\d", "gu", @"(?:\d\.){3}\d", System.Text.RegularExpressions.RegexOptions.None),
            (@"Шерлок", "giu", @"Шерлок", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"holmes", "giu", @"holmes", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            // (\b is not the same in .NET, whose word characters take every script, so it is not compared here.)
        })
        {
            int expected = System.Text.RegularExpressions.Regex.Count(text, netPattern, options);
            Regex r = new(pattern, flags);
            Assert.AreEqual(expected, r.Count(text), pattern + " UTF-16");
            Assert.AreEqual(expected, r.Count(utf8), pattern + " UTF-8");
        }
    }
}
