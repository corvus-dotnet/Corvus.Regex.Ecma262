---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Setup"
---
## Setup

One package carries the library and the source generator:

```bash
dotnet add package Corvus.Regex.Ecma262
```

The library targets `net10.0` and `netstandard2.0`. The vectorised searches and the generated search code need the .NET 10 or later runtime; a project on an earlier runtime, or on .NET Framework, takes the `netstandard2.0` build, where the same patterns give the same answers by the plain paths.

```csharp
using Corvus.Ecma262.RegularExpressions;
```
