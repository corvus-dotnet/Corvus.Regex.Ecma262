// <copyright file="Diagnostics.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.CodeAnalysis;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator;

/// <summary>The diagnostics the generator reports.</summary>
internal static class Diagnostics
{
    private const string Category = "Corvus.Regex.Ecma262";

    /// <summary>The pattern is not valid in the grammar of its flags: the ECMA-262 message and the offset.</summary>
    public static readonly DiagnosticDescriptor InvalidPattern = new(
        "CRE0001",
        "The pattern is not a valid ECMA-262 regular expression",
        "Invalid regular expression: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The member is not a static partial method or property with no parameters that returns <c>Regex</c>.</summary>
    public static readonly DiagnosticDescriptor WrongMemberShape = new(
        "CRE0002",
        "The member cannot be generated",
        "The [GeneratedRegex] member must be a static partial method with no parameters, or a static partial property, that returns Corvus.Ecma262.RegularExpressions.Regex: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The flags are not valid.</summary>
    public static readonly DiagnosticDescriptor InvalidFlags = new(
        "CRE0003",
        "The flags are not valid",
        "Invalid flags: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>A type that holds the member is not partial.</summary>
    public static readonly DiagnosticDescriptor TypeNotPartial = new(
        "CRE0004",
        "The containing type must be partial",
        "The type '{0}' that holds the [GeneratedRegex] member must be partial",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The pattern is over a limit of size.</summary>
    public static readonly DiagnosticDescriptor PatternTooLarge = new(
        "CRE0005",
        "The pattern is too large",
        "The pattern is too large: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
