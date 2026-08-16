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
            options.Converters.Add(new JsonStringEnumConverter());
        }

        return JsonTypeInfoLookup.WithReflectionFallback(options);
    }
}