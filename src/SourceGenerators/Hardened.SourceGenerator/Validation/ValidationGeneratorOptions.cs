using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ValidationModules.SourceGenerator.Impl;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// The MSBuild properties that decide what a validator looks like, read once and shared by every
/// generator that builds one.
/// </summary>
/// <remarks>
/// <para>
/// The same properties <c>ValidationModules.SourceGenerator</c> reads, because two generators have
/// to reach the same answer about the same type. The handler generators ask "does this body model
/// get a validator, and what is it called" so they can emit a call to it; ValidationModules'
/// generator answers "yes" by emitting it. Those are separate assemblies reading the same
/// compilation, and the only thing keeping them in step is that the question is asked with
/// identical inputs. A field namer that differed between them would not fail - it would quietly
/// report a different field name depending on which generator produced the validator.
/// </para>
/// <para>
/// A wrong answer about <em>existence</em> is loud: the handler names a validator that was never
/// emitted and the compilation fails on the missing type. That is the intended failure mode, and
/// the reason this file is one file rather than two agreeing implementations.
/// </para>
/// </remarks>
public sealed record ValidationGeneratorOptions(
    string? Naming,
    string? DataAnnotations,
    string? PatternPolicySetting,
    bool IsAotFacing,
    bool GeneratorReferenced = false
)
{
    public static ValidationGeneratorOptions Read(AnalyzerConfigOptionsProvider provider)
    {
        provider.GlobalOptions.TryGetValue(
            "build_property.ValidationModules_FieldNaming",
            out var naming
        );
        provider.GlobalOptions.TryGetValue(
            "build_property.ValidationModules_DataAnnotations",
            out var dataAnnotations
        );
        provider.GlobalOptions.TryGetValue(
            "build_property.ValidationModules_PatternPolicy",
            out var patternPolicy
        );
        provider.GlobalOptions.TryGetValue("build_property.PublishAot", out var publishAot);
        provider.GlobalOptions.TryGetValue("build_property.IsAotCompatible", out var aotCompatible);

        return new ValidationGeneratorOptions(
            naming,
            dataAnnotations,
            patternPolicy,
            IsTrue(publishAot) || IsTrue(aotCompatible),
            provider.GlobalOptions.TryGetValue(GeneratorProperty, out _)
        );
    }

    /// <summary>
    /// A property <c>ValidationModules.SourceGenerator</c>'s package declares, which is how a
    /// handler generator learns that validators are being emitted for this compilation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A handler generator emits a call to a validator it cannot see, on the strength of
    /// ValidationModules emitting one for the same type. If that generator is not referenced,
    /// nothing emits it and the compilation fails on a type that does not exist - in a project that
    /// never asked for validation and whose models merely carry
    /// <c>System.ComponentModel.DataAnnotations</c> attributes for some other reason.
    /// </para>
    /// <para>
    /// The package's targets list it as a <c>CompilerVisibleProperty</c>, and the build writes the
    /// key whether or not it has a value. So its presence answers the question: one generator's
    /// regular output is invisible to another, and this is not output at all.
    /// </para>
    /// </remarks>
    public const string GeneratorProperty = "build_property.ValidationModules_Registration";

    /// <summary>
    /// The name of the validator emitted for a type, which is a convention rather than a message
    /// passed between the two generators.
    /// </summary>
    /// <remarks>
    /// The build task hands the generator every name it invents, because a task and a generator
    /// that derive the same name separately drift the moment one of them changes. This is the
    /// opposite case: there is no channel between two Roslyn generators to pass a name along, so
    /// the convention is the channel - and it is ValidationModules' own, compiled in from the same
    /// package, so the two cannot name a type differently. It is safe only because getting it wrong
    /// cannot compile.
    /// </remarks>
    public static Func<INamedTypeSymbol, string> ValidatorNameFor { get; } =
        GeneratedNames.Validator;

    /// <summary>
    /// Auto gates on the project's own AOT posture rather than on PublishAot alone, which is
    /// only ever true in an executable - a class library holding the models would never see it.
    /// </summary>
    public PatternPolicy ResolvedPatternPolicy =>
        PatternPolicySetting switch
        {
            "Error" => PatternPolicy.Error,
            "Warn" => PatternPolicy.Warn,
            "Allow" => PatternPolicy.Allow,
            _ => IsAotFacing ? PatternPolicy.Error : PatternPolicy.Allow,
        };

    public bool CompileDataAnnotations =>
        !string.Equals(DataAnnotations, "Ignore", StringComparison.OrdinalIgnoreCase);

    public Func<string, string> FieldNamer =>
        Naming switch
        {
            "PascalCase" or "AsDeclared" => static name => name,
            "SnakeCase" => SnakeCase,
            _ => CamelCase,
        };

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string CamelCase(string name) =>
        name.Length == 0 || !char.IsUpper(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string SnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];

            if (char.IsUpper(character))
            {
                var startsWord =
                    i > 0
                    && (
                        !char.IsUpper(name[i - 1])
                        || (i + 1 < name.Length && char.IsLower(name[i + 1]))
                    );

                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
