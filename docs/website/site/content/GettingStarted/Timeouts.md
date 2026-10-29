---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-29T00:00:00.0+00:00
Title: "Timeouts"
---
## Timeouts

A match has no limit by default. A `Regex` made with a timeout (`MatchTimeout`) abandons a match that reaches it, or that reaches the cap on backtracking memory the timeout brings, with `RegexMatchTimeoutException`, whatever the pattern:

```csharp
Regex slow = new(@"^(?:(a+)\1*)+$", RegexFlags.None, TimeSpan.FromMilliseconds(100));
try
{
    slow.IsMatch(new string('a', 60) + "b");
}
catch (RegexMatchTimeoutException)
{
}
```

An instance made without a timeout takes `Regex.DefaultMatchTimeout`: the `REGEX_DEFAULT_MATCH_TIMEOUT` data of the `AppContext`, which `System.Text.RegularExpressions` reads too, and no timeout when it is not set. A program that matches patterns or texts from outside should set one, for example with `AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1))` when it starts.

A pattern without a backreference is matched in time linear in the text, and no pattern can overflow the stack, so the timeout is what bounds the others.
