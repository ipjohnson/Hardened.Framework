using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Hardened.SourceGeneration.Testing;

/// <summary>
/// MSBuild properties as a generator sees them.
/// </summary>
/// <remarks>
/// Hardened generators read several: <c>RootNamespace</c> everywhere,
/// <c>HardenedOpenApiNamespace</c> and <c>ExcludeGeneratedCodeFromCoverage</c> in the OpenAPI
/// generator, and <c>ProjectDir</c> wherever a file's location decides ownership. A test that does
/// not supply them gets the defaults below rather than nothing, so the common case needs no setup.
/// </remarks>
internal sealed class TestAnalyzerConfigOptionsProvider(
    IReadOnlyDictionary<string, string>? buildProperties,
    string projectDir)
    : AnalyzerConfigOptionsProvider {

    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new TestAnalyzerConfigOptions(buildProperties, projectDir);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions {
    private readonly Dictionary<string, string> _options;

    public TestAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string>? buildProperties,
        string projectDir) {
        _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["build_property.RootNamespace"] = "TestNamespace",
            ["build_property.ProjectDir"] = projectDir
        };

        if (buildProperties != null) {
            foreach (var pair in buildProperties) {
                _options["build_property." + pair.Key] = pair.Value;
            }
        }
    }

    public override bool TryGetValue(string key, out string value) =>
        _options.TryGetValue(key, out value!);
}

/// <summary>
/// An <c>AdditionalFiles</c> entry held in memory.
/// </summary>
/// <remarks>
/// Hardened needs this where DependencyModules did not: OpenAPI specifications and templates reach
/// their generators only through <c>AdditionalTextsProvider</c>, so neither generator can be tested
/// at all without it.
/// </remarks>
internal sealed class TestAdditionalText(string path, string text) : AdditionalText {
    private readonly SourceText _text = SourceText.From(text);

    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
