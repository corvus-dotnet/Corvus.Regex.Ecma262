// <copyright file="RegexGenerator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Corvus.Ecma262.RegularExpressions.SourceGenerator;

/// <summary>
/// The generator for <c>[GeneratedRegex]</c>: a <c>static partial</c> method or property that returns a
/// <c>Regex</c> is checked at build time (the pattern is parsed with the library's own parser, so a pattern that
/// is not valid is a build error with the ECMA-262 message and the offset) and given its body, which returns one
/// <c>Regex</c> held in a nested static class and made the first time it is used.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RegexGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Corvus.Ecma262.RegularExpressions.GeneratedRegexAttribute";
    private const string RegexTypeName = "Corvus.Ecma262.RegularExpressions.Regex";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Target> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
            static (ctx, _) => Collect(ctx));

        context.RegisterSourceOutput(targets, static (spc, target) => Emit(spc, target));
    }

    private static Target Collect(GeneratorAttributeSyntaxContext ctx)
    {
        ISymbol symbol = ctx.TargetSymbol;
        SyntaxNode node = ctx.TargetNode;
        Location location = node.GetLocation();
        List<Diagnostic> diagnostics = [];
        AttributeData attribute = ctx.Attributes[0];
        Location patternLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? location;

        // The member: a static partial method with no parameters, or a static partial property, returning Regex.
        string? problem = null;
        bool isProperty = symbol is IPropertySymbol;
        ITypeSymbol? returns = symbol switch
        {
            IMethodSymbol m => m.ReturnType,
            IPropertySymbol prop => prop.Type,
            _ => null,
        };
        if (symbol is IMethodSymbol method)
        {
            if (!method.IsPartialDefinition)
            {
                problem = "the method is not partial";
            }
            else if (method.Parameters.Length != 0)
            {
                problem = "the method has parameters";
            }
            else if (method.IsGenericMethod)
            {
                problem = "the method is generic";
            }
        }
        else if (symbol is IPropertySymbol property)
        {
            if (!property.IsPartialDefinition)
            {
                problem = "the property is not partial";
            }
            else if (property.SetMethod is not null)
            {
                problem = "the property has a setter";
            }
        }

        if (problem is null && !symbol.IsStatic)
        {
            problem = "the member is not static";
        }

        if (problem is null && returns?.ToDisplayString() != RegexTypeName)
        {
            problem = "the member does not return " + RegexTypeName;
        }

        if (problem is not null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.WrongMemberShape, location, problem));
        }

        // The types that hold it, innermost first, each partial.
        List<string> containers = [];
        for (INamedTypeSymbol? type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            bool partial = false;
            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    partial = true;
                    break;
                }
            }

            if (!partial)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.TypeNotPartial, location, type.Name));
            }

            containers.Add(Declaration(type));
        }

        // The attribute's arguments: pattern, flags as the enum or as letters, and the timeout.
        string pattern = string.Empty;
        int flags = 0;
        string? flagsText = null;
        int timeout = -1;
        string runners = string.Empty;
        ImmutableArray<TypedConstant> args = attribute.ConstructorArguments;
        if (args.Length >= 1 && args[0].Value is string p)
        {
            pattern = p;
        }

        if (args.Length >= 2)
        {
            if (args[1].Value is string letters)
            {
                flagsText = letters;
                flags = ParseFlags(letters, out string? bad);
                if (bad is not null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidFlags, patternLocation, bad));
                }
            }
            else if (args[1].Value is int value)
            {
                flags = value;
            }
        }

        if (args.Length >= 3 && args[2].Value is int ms)
        {
            timeout = ms;
        }

        if (problem is null && (flags & ~0xFF) == 0)
        {
            // The pattern, parsed and compiled as the library parses and compiles it, and its programs written
            // out as straight-line code.
            try
            {
                runners = Runners(pattern, flags);
            }
            catch (RegexPatternTooLargeException e)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.PatternTooLarge, patternLocation, e.Message));
            }
            catch (RegexParseException e)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidPattern, patternLocation, e.Message));
            }
        }

        string ns = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString();
        string modifiers = node switch
        {
            MethodDeclarationSyntax m => m.Modifiers.ToString(),
            PropertyDeclarationSyntax pd => pd.Modifiers.ToString(),
            _ => "static partial",
        };

        return new Target(
            ns,
            containers.ToImmutableArray(),
            symbol.Name,
            isProperty,
            modifiers,
            pattern,
            flags,
            flagsText,
            timeout,
            runners,
            diagnostics.ToImmutableArray());
    }

    /// <summary>The two programs of a pattern (the bounds of a match, and every group) as runner classes.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <returns>The text of the classes, indented for a nested class.</returns>
    internal static string Runners(string pattern, int flags)
    {
        bool unicode = (flags & (int)(RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0;
        EcmaRegexParser parser = new(
            pattern.AsSpan(),
            unicode,
            ignoreCase: (flags & (int)RegexFlags.IgnoreCase) != 0,
            multiline: (flags & (int)RegexFlags.Multiline) != 0,
            dotAll: (flags & (int)RegexFlags.DotAll) != 0,
            unicodeSets: (flags & (int)RegexFlags.UnicodeSets) != 0);
        EcmaRegexNode root = parser.Parse();
        Program bounds = Compiler.Compile(root, unicode, parser.GroupCount, trackAll: false);
        Program captures = Compiler.Compile(root, unicode, parser.GroupCount, trackAll: true);
        StringBuilder sb = new();
        if (!TreeEmitter.TryEmitRunner(sb, 1, "Bounds", bounds))
        {
            RunEmitter.EmitRunner(sb, 1, "Bounds", bounds);
        }

        sb.AppendLine();
        if (!TreeEmitter.TryEmitRunner(sb, 1, "Captures", captures))
        {
            RunEmitter.EmitRunner(sb, 1, "Captures", captures);
        }

        return sb.ToString();
    }

    private static string Declaration(INamedTypeSymbol type)
    {
        string kind = type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            _ => type.IsRecord ? "record" : "class",
        };
        StringBuilder sb = new();
        sb.Append("partial ").Append(kind).Append(' ').Append(type.Name);
        if (type.TypeParameters.Length > 0)
        {
            sb.Append('<');
            for (int k = 0; k < type.TypeParameters.Length; k++)
            {
                if (k > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(type.TypeParameters[k].Name);
            }

            sb.Append('>');
        }

        return sb.ToString();
    }

    private static int ParseFlags(string letters, out string? bad)
    {
        bad = null;
        int result = 0;
        foreach (char c in letters)
        {
            int flag = c switch
            {
                'd' => (int)RegexFlags.HasIndices,
                'g' => (int)RegexFlags.Global,
                'i' => (int)RegexFlags.IgnoreCase,
                'm' => (int)RegexFlags.Multiline,
                's' => (int)RegexFlags.DotAll,
                'u' => (int)RegexFlags.Unicode,
                'v' => (int)RegexFlags.UnicodeSets,
                'y' => (int)RegexFlags.Sticky,
                _ => 0,
            };
            if (flag == 0)
            {
                bad = "'" + c + "' is not a flag (the flags are d, g, i, m, s, u, v and y)";
                return 0;
            }

            if ((result & flag) != 0)
            {
                bad = "the flag '" + c + "' is given twice";
                return 0;
            }

            result |= flag;
        }

        if ((result & (int)RegexFlags.Unicode) != 0 && (result & (int)RegexFlags.UnicodeSets) != 0)
        {
            bad = "the u and v flags cannot both be given";
            return 0;
        }

        return result;
    }

    private static void Emit(SourceProductionContext spc, Target target)
    {
        foreach (Diagnostic d in target.Diagnostics)
        {
            spc.ReportDiagnostic(d);
        }

        if (target.Diagnostics.Length > 0)
        {
            return;
        }

        StringBuilder sb = new();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS0162, CS0164, CS0168, CS0219, CS8321, CS1522");
        sb.AppendLine();
        int indent = 0;
        if (target.Namespace.Length > 0)
        {
            sb.Append("namespace ").AppendLine(target.Namespace);
            sb.AppendLine("{");
            indent++;
        }

        // The containers, outermost first.
        for (int k = target.Containers.Length - 1; k >= 0; k--)
        {
            Indent(sb, indent).AppendLine(target.Containers[k]);
            Indent(sb, indent).AppendLine("{");
            indent++;
        }

        string holder = "CorvusRegex_" + target.Member;
        string flags = "(global::Corvus.Ecma262.RegularExpressions.RegexFlags)" + target.Flags.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string timeout = target.TimeoutMilliseconds < 0
            ? "global::Corvus.Ecma262.RegularExpressions.Regex.InfiniteMatchTimeout"
            : "global::System.TimeSpan.FromMilliseconds(" + target.TimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        string pattern = SymbolDisplay.FormatLiteral(target.Pattern, quote: true);

        Indent(sb, indent).AppendLine("/// <summary>The regular expression " + Comment(target) + ", made once.</summary>");
        Indent(sb, indent).AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
        if (target.IsProperty)
        {
            Indent(sb, indent).Append(target.Modifiers).Append(" global::Corvus.Ecma262.RegularExpressions.Regex ").Append(target.Member)
                .Append(" => ").Append(holder).AppendLine(".Instance;");
        }
        else
        {
            Indent(sb, indent).Append(target.Modifiers).Append(" global::Corvus.Ecma262.RegularExpressions.Regex ").Append(target.Member)
                .Append("() => ").Append(holder).AppendLine(".Instance;");
        }

        sb.AppendLine();
        Indent(sb, indent).AppendLine("/// <summary>Holds the regular expression " + Comment(target) + ".</summary>");
        Indent(sb, indent).AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Corvus.Regex.Ecma262.SourceGenerator\", \"1.0\")]");
        Indent(sb, indent).Append("private static class ").AppendLine(holder);
        Indent(sb, indent).AppendLine("{");
        Indent(sb, indent + 1).AppendLine("/// <summary>The regular expression.</summary>");
        Indent(sb, indent + 1).Append("internal static readonly global::Corvus.Ecma262.RegularExpressions.Regex Instance = new(")
            .Append(pattern).Append(", ").Append(flags).Append(", ").Append(timeout).AppendLine(", new Bounds(), new Captures());");
        sb.AppendLine();
        foreach (string line in target.Runners.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                sb.AppendLine();
            }
            else
            {
                Indent(sb, indent).AppendLine(trimmed);
            }
        }

        Indent(sb, indent).AppendLine("}");

        for (int k = 0; k < target.Containers.Length; k++)
        {
            indent--;
            Indent(sb, indent).AppendLine("}");
        }

        if (target.Namespace.Length > 0)
        {
            sb.AppendLine("}");
        }

        string name = (target.Namespace.Length > 0 ? target.Namespace + "." : string.Empty) + Path(target) + "." + target.Member + ".g.cs";
        spc.AddSource(name, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Comment(Target target)
    {
        string text = target.Pattern.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return "<c>/" + text + "/" + (target.FlagsText ?? Letters(target.Flags)) + "</c>";
    }

    private static string Letters(int flags)
    {
        StringBuilder sb = new();
        foreach ((RegexFlags flag, char letter) in new[] { (RegexFlags.HasIndices, 'd'), (RegexFlags.Global, 'g'), (RegexFlags.IgnoreCase, 'i'), (RegexFlags.Multiline, 'm'), (RegexFlags.DotAll, 's'), (RegexFlags.Unicode, 'u'), (RegexFlags.UnicodeSets, 'v'), (RegexFlags.Sticky, 'y') })
        {
            if ((flags & (int)flag) != 0)
            {
                sb.Append(letter);
            }
        }

        return sb.ToString();
    }

    private static string Path(Target target)
    {
        StringBuilder sb = new();
        for (int k = target.Containers.Length - 1; k >= 0; k--)
        {
            string c = target.Containers[k];
            int space = c.LastIndexOf(' ');
            string name = c.Substring(space + 1);
            int angle = name.IndexOf('<');
            if (angle >= 0)
            {
                name = name.Substring(0, angle);
            }

            if (sb.Length > 0)
            {
                sb.Append('.');
            }

            sb.Append(name);
        }

        return sb.ToString();
    }

    private static StringBuilder Indent(StringBuilder sb, int level)
    {
        for (int k = 0; k < level; k++)
        {
            sb.Append("    ");
        }

        return sb;
    }

    private sealed record Target(
        string Namespace,
        ImmutableArray<string> Containers,
        string Member,
        bool IsProperty,
        string Modifiers,
        string Pattern,
        int Flags,
        string? FlagsText,
        int TimeoutMilliseconds,
        string Runners,
        ImmutableArray<Diagnostic> Diagnostics);
}
