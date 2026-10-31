# Corvus.Regex.Ecma262.CodeGeneration.Sources

The [Corvus.Regex.Ecma262](https://www.nuget.org/packages/Corvus.Regex.Ecma262) emitter as source, for a code
generator that writes the library's generated code for a pattern into the code it generates.

A source generator does not run on another generator's output, so a `[GeneratedRegex]` member in generated code is
never given its body. A code generator that takes this package writes the body itself: the same compiled matcher the
library's source generator writes.

```xml
<PackageReference Include="Corvus.Regex.Ecma262.CodeGeneration.Sources" PrivateAssets="all" />
```

The package has no assembly. Its C# is compiled into the project that takes it, with its types `internal` and in
the namespace `Corvus.Ecma262.RegularExpressions.CodeGeneration`, so it loads nothing beside a source generator and
never clashes with the library in a project that references both. It needs C# 12 or later (`LangVersion` 12) and,
on .NET Standard 2.0, `System.Memory`, which it brings as a dependency. It compiles into a project on .NET 10 or
later, or on .NET Standard 2.0 (as a Roslyn source generator or a .NET Framework project is). A project on .NET 8 or
9 is refused with the error `CRES0001`.

The emitter's settings are the package's own copies of the library's enums, in its namespace, so a file that uses
both namespaces names them in full.

```csharp
using Corvus.Ecma262.RegularExpressions.CodeGeneration;

if (RegexEmitter.TryEmit(@"^[a-z][a-z0-9_]{0,29}$", RegexFlags.Unicode, RegexEdition.Es2020, RegexUnicodeVersion.Default, "UserName", out string? source, out string? error))
{
    // source is "private static class UserName { internal static readonly Regex Instance = ...; ... }",
    // to nest in a generated type: UserName.Instance is the regular expression.
}
else
{
    // error is the message Regex.TryCreate would give.
}
```

`RegexEmitter.TryValidate` reads and compiles a pattern with nothing written, and says whether it is not valid or
valid and over a limit of size, as the library's `Regex.TryCreate` and `RegexPatternTooLargeException` would. It is
for a pattern the code generator checks but does not emit code for.

The generated code references the library, so the project that compiles it references
`Corvus.Regex.Ecma262` of the same version.
