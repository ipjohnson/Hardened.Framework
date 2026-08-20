using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Json;

public interface IJsonSerializerConfiguration {
    JsonSerializerOptions Options { get; }
}

public class JsonSerializerConfiguration : IJsonSerializerConfiguration {
    public JsonSerializerOptions Options { get; set; } = DefaultConfiguration();

    // The IsDynamicCodeSupported guard below is what makes this correct, and ILC honours it - it
    // treats the property as a constant and removes the branch, so an AOT publish never reaches
    // the converter. The Roslyn analyzer shipped for net8.0 does not recognise that guard yet, so
    // it reports the call anyway. Suppressed rather than annotated: annotating would push a
    // RequiresDynamicCode onto every caller of a method that is, in fact, AOT-safe.
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Guarded by RuntimeFeature.IsDynamicCodeSupported; ILC removes the branch.")]
    private static JsonSerializerOptions DefaultConfiguration() {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            AllowTrailingCommas = true
        };

        // Enums as names rather than numbers, where that is possible.
        //
        // The non-generic JsonStringEnumConverter builds a converter per enum at run time, which
        // is the one thing Native AOT does not have. There is no non-generic AOT-safe equivalent:
        // JsonStringEnumConverter<TEnum> needs the enum at compile time, and this configuration
        // does not know any of them.
        //
        // Guarded on IsDynamicCodeSupported rather than annotated, because ILC treats that property
        // as a constant and removes the branch outright - so an AOT publish carries no converter,
        // no warning and no suppression claiming something untrue. A JIT application is unaffected.
        //
        // An AOT application that wants named enums says so where it can be compiled:
        // [JsonSourceGenerationOptions(UseStringEnumConverter = true)] on its JsonSerializerContext.
        if (RuntimeFeature.IsDynamicCodeSupported) {
            options.Converters.Add(new DeclaredEnumsFirstConverter());
        }

        return JsonTypeInfoLookup.WithReflectionFallback(options);
    }
}

/// <summary>
/// Names for enums that have no converter of their own, and stands aside for those that do.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>JsonStringEnumConverter</c> sat here, and it broke every enum a description declared.
/// System.Text.Json ranks a converter in <c>Options.Converters</c> <em>above</em> a
/// <c>[JsonConverter]</c> attribute on the type, so the generated converter - the one that knows the
/// document's values - never ran through this serializer. It wrote the C# member name:
/// <c>{"genre":"ScienceFiction"}</c> where the document says <c>"science-fiction"</c>.
/// </para>
/// <para>
/// It bit hardest in the test host, whose <c>Post(object, path)</c> serializes with exactly this
/// service while the request deserializer builds its own options and honours the attribute. So the
/// harness wrote a value its own application then refused, and every test posting a generated record
/// carrying an enum got a 500.
/// </para>
/// <para>
/// Declining by <c>CanConvert</c> rather than dropping the converter outright: an application's own
/// enums, which no description declared and nothing generated a converter for, still serialize as
/// names rather than as numbers. Only the enums that already know their own wire form are left
/// alone.
/// </para>
/// </remarks>
internal sealed class DeclaredEnumsFirstConverter : JsonConverterFactory {

    /// <summary>
    /// An enum that has not already said how it is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test is spelled out rather than delegated to a held <c>JsonStringEnumConverter</c>.
    /// Constructing one in a field initializer is IL3050 whatever this class does with it, because
    /// the field runs outside the <c>IsDynamicCodeSupported</c> guard that makes the rest of this
    /// safe - and a suppression there would be claiming something untrue.
    /// </para>
    /// <para>
    /// <c>Nullable&lt;TEnum&gt;</c> is deliberately <em>not</em> claimed, which is what the built-in
    /// converter does too: System.Text.Json unwraps a nullable and asks about the underlying type,
    /// so answering true here wins a type this cannot then build a converter for.
    /// </para>
    /// </remarks>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum &&
        !typeToConvert.IsDefined(typeof(JsonConverterAttribute), inherit: false);

    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Only reached from DefaultConfiguration, which guards on " +
                        "RuntimeFeature.IsDynamicCodeSupported; ILC removes that branch.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Same guard: an AOT publish never reaches this factory.")]
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new JsonStringEnumConverter().CreateConverter(typeToConvert, options);

}