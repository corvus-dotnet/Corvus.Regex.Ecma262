---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Matching"
---
## Matching

The members are the ones of `System.Text.RegularExpressions.Regex`, with the same names and the same shapes:

```csharp
Regex regex = new(@"(?<word>\w+)@(?<host>\w+)\.\w+", "gu");

bool any = regex.IsMatch("mail sherlock@baker.st today");
Match m = regex.Match("mail sherlock@baker.st today");
string host = m.Groups["host"].Value;                       // "baker"
MatchCollection all = regex.Matches("a@b.c and d@e.f");     // 2 matches
string replaced = regex.Replace("a@b.c", "$<word> at $<host>"); // "a at b"
string?[] parts = regex.Split("x a@b.c y");
```

`Match`, `Groups`, `Matches`, `Replace` with a string and `Split` with a string allocate what they return, as .NET's do: that is the drop-in shape. The span members beside them allocate nothing:

```csharp
// Every match, with the groups into a span the caller gives.
Span<ValueCapture> groups = stackalloc ValueCapture[regex.GroupCount + 1];
foreach (ValueMatch match in regex.EnumerateMatches(text.AsSpan(), groups))
{
    ReadOnlySpan<char> host = text.AsSpan(groups[2].Index, groups[2].Length);
}

// One match from a position.
if (regex.TryMatch(text.AsSpan(), 0, groups, out ValueMatch first)) { }

// Replace into a span, or into a buffer writer.
Span<char> buffer = stackalloc char[256];
if (regex.TryReplace(text.AsSpan(), "$<word>".AsSpan(), buffer, out int written)) { }
regex.Replace(text.AsSpan(), "$<word>".AsSpan(), new ArrayBufferWriter<char>());

// Split without the array.
foreach (Range piece in regex.EnumerateSplits(text.AsSpan())) { }

int n = regex.Count(text);
```

`Matches`, `EnumerateMatches` and `Count` are the global search whether or not `g` is set. Where ECMA-262 keeps `lastIndex` on the `RegExp`, the library takes the position as an argument: `Match(input, startAt)` is `exec` with `lastIndex` set, and `y` anchors it there. A `Regex` has no mutable state, so one instance is safe to share between threads.
