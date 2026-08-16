using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hardened.Idl.Validation;

/// <summary>
/// The regular expressions a spec declares, and the <c>[GeneratedRegex]</c> member each one becomes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what the build task is for.</b> A source generator cannot emit
/// <c>[GeneratedRegex]</c>: its output is not in the compilation the regex generator reads, so the
/// partial method is never implemented and the consumer's build fails with CS8795. A task writes
/// ordinary source into <c>@(Compile)</c> before the compiler runs, which the regex generator sees
/// like any other file. The alternative is a runtime-constructed <c>Regex</c>, which costs 448 KB on
/// an AOT publish against 33 KB for this - and it is a threshold, not a per-pattern price, because
/// what is being paid for is rooting the regex parser and interpreter at all.
/// </para>
/// <para>
/// <b>The task names the members and writes the names into the model.</b> Nothing derives a name
/// twice. Identical patterns collapse onto one member for free, which is the same reason the spike
/// this follows named them by hash rather than by the property they came from.
/// </para>
/// </remarks>
internal sealed class PatternRegistry {
    private readonly Dictionary<string, string> _members = new(System.StringComparer.Ordinal);
    private readonly List<string> _rejected = new();
    private readonly string _namespace;

    public PatternRegistry(string patternNamespace, string specFileName) {
        _namespace = patternNamespace;
        ClassName = Hardened.Idl.NamingHelper.ToPascalCase(specFileName) + "Patterns";
    }

    /// <summary>The class the members are declared on.</summary>
    public string ClassName { get; }

    /// <summary>Pattern to member name, in insertion order.</summary>
    public IReadOnlyDictionary<string, string> Members => _members;

    public bool IsEmpty => _members.Count == 0;

    /// <summary>Patterns the runtime's engine will not accept, with the reason.</summary>
    public IReadOnlyList<string> Rejected => _rejected;

    /// <summary>
    /// The arguments for a <c>[Pattern]</c> in its reference form, declaring a
    /// <c>[GeneratedRegex]</c> member for <paramref name="pattern"/> on first sighting.
    /// </summary>
    /// <remarks>
    /// <c>[Pattern(typeof(X), nameof(X.Y))]</c> rather than <c>[Pattern("...")]</c>. The inline form
    /// makes the generator declare a <c>Regex</c> itself, which roots the parser and the interpreter
    /// - 448 KB on an AOT publish - and is what VM0017 rejects in an AOT-facing project. The
    /// referenced member is resolved at generation time, so a typo is VM0018 rather than something
    /// found later.
    /// </remarks>
    /// <returns>Null when the pattern is not one .NET can compile - see <see cref="Rejected"/>.</returns>
    public System.Collections.Generic.IReadOnlyList<string>? AttributeArguments(string pattern) {
        // OpenAPI specifies ECMA-262, and .NET's engine is not a superset of it. Grafana declares
        // ^[a-zA-Z0-9\-\_]+$, where \_ is an ordinary escaped underscore in ECMA-262 and an
        // unrecognized escape sequence here. Emitted anyway it reaches [GeneratedRegex], which
        // fails to generate, leaving its partial method unimplemented - CS8795 in a generated file,
        // for a pattern the document was entitled to write.
        if (!Compiles(pattern)) {
            return null;
        }

        var member = Member(pattern);
        var qualified = $"global::{_namespace}.{ClassName}";

        return new[] { $"typeof({qualified})", $"nameof({qualified}.{member})" };
    }

    private bool Compiles(string pattern) {
        try {
            _ = new Regex(pattern);
            return true;
        } catch (System.ArgumentException exception) {
            if (!_rejected.Exists(entry => entry.StartsWith(pattern, System.StringComparison.Ordinal))) {
                _rejected.Add(pattern + " - " + exception.Message);
            }

            return false;
        }
    }

    private string Member(string pattern) {
        if (!_members.TryGetValue(pattern, out var member)) {
            member = "P_" + Hash(pattern);
            _members.Add(pattern, member);
        }

        return member;
    }

    /// <summary>
    /// A stable, short name for a pattern.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than <see cref="string.GetHashCode"/>, which is randomised per process in
    /// .NET Core - the member name has to be the same on every build or the emitted file churns and
    /// every consumer recompiles.
    /// </remarks>
    private static string Hash(string value) {
        unchecked {
            var hash = 2166136261;

            foreach (var character in value) {
                hash = (hash ^ character) * 16777619;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
