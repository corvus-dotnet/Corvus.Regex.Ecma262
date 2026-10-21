// <copyright file="Deviations.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text.Json;

/// <summary>
/// The records and tests marked v8_deviation, where V8's answers depart from ECMA-262. They are left out, but for
/// those of the class <c>unicode-version</c>, where V8 has the Unicode of 16 and test262 that of 17: the library has
/// Unicode 17, so they are judged by test262's verdicts (<c>test262_valid</c>) and answers (<c>test262_answer</c>)
/// in the grammars those name, and left out in the others.
/// </summary>
internal static class Deviations
{
    /// <summary>Whether a record or a test is marked v8_deviation.</summary>
    /// <param name="e">The record or the test.</param>
    /// <returns><see langword="true"/> if it is marked.</returns>
    public static bool IsMarked(JsonElement e) => e.TryGetProperty("v8_deviation", out JsonElement dev) && dev.GetBoolean();

    /// <summary>Whether a marked record or test is judged by test262's answers: one of the Unicode version.</summary>
    /// <param name="marked">The marked record or test.</param>
    /// <returns><see langword="true"/> if it is judged by test262.</returns>
    public static bool ByTest262(JsonElement marked) => marked.TryGetProperty("deviation", out JsonElement d) && d.ValueKind == JsonValueKind.String && d.GetString() == "unicode-version";

    /// <summary>Whether a record is left out: one marked v8_deviation that is not judged by test262's answers.</summary>
    /// <param name="r">The record.</param>
    /// <returns><see langword="true"/> if it is left out.</returns>
    public static bool IsSkipped(JsonElement r) => IsMarked(r) && !ByTest262(r);

    /// <summary>Whether a record is judged by test262's verdicts: one marked for the Unicode version.</summary>
    /// <param name="r">The record.</param>
    /// <returns><see langword="true"/> if it is judged by test262.</returns>
    public static bool IsRecordByTest262(JsonElement r) => IsMarked(r) && ByTest262(r);

    /// <summary>
    /// The verdict a record is judged by in a grammar: V8's, or test262's for a record marked for the Unicode version,
    /// which is not judged in a grammar test262 gives no verdict for.
    /// </summary>
    /// <param name="r">The record.</param>
    /// <param name="answerName">The name of the grammar's answers: <c>unicode</c>, <c>legacy</c> or <c>unicode_sets</c>.</param>
    /// <param name="v8Valid">Whether V8 found the pattern valid in the grammar.</param>
    /// <param name="expectedValid">The verdict the record is judged by.</param>
    /// <returns><see langword="false"/> if the record is not judged in the grammar.</returns>
    public static bool TryGetExpectedValid(JsonElement r, string answerName, bool v8Valid, out bool expectedValid)
    {
        if (!IsRecordByTest262(r))
        {
            expectedValid = v8Valid;
            return true;
        }

        if (r.TryGetProperty("test262_valid", out JsonElement verdicts) && verdicts.TryGetProperty(answerName, out JsonElement verdict))
        {
            expectedValid = verdict.GetBoolean();
            return true;
        }

        expectedValid = false;
        return false;
    }

    /// <summary>
    /// The answer a test is judged by in a grammar: V8's or, for a test marked for the Unicode version or any test of a
    /// record so marked, test262's. A test marked otherwise, or with no answer in the grammar, is left out.
    /// </summary>
    /// <param name="t">The test.</param>
    /// <param name="recordByTest262">Whether the test's record is marked for the Unicode version.</param>
    /// <param name="answerName">The name of the grammar's answers.</param>
    /// <param name="expected">The answer the test is judged by.</param>
    /// <returns><see langword="false"/> if the test is not judged in the grammar.</returns>
    public static bool TryGetExpected(JsonElement t, bool recordByTest262, string answerName, out bool expected)
    {
        expected = false;
        bool marked = IsMarked(t);
        if (marked && !ByTest262(t))
        {
            return false;
        }

        JsonElement answers = t;
        if ((marked || recordByTest262) && !t.TryGetProperty("test262_answer", out answers))
        {
            return false;
        }

        if (!answers.TryGetProperty(answerName, out JsonElement answer))
        {
            return false;
        }

        expected = answer.GetBoolean();
        return true;
    }
}
