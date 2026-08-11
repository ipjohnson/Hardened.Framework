using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGeneration.Testing;

/// <summary>
/// The metadata references a generator's test source needs to compile.
///
/// <para>
/// Built from the runtime's trusted platform assembly list rather than from whatever happens to be
/// loaded. The loaded-assembly approach works until it does not: assemblies load lazily, so whether
/// a reference is present depends on what ran earlier in the process, and a suite that passes alone
/// starts failing when another test happens to run first.
/// </para>
/// </summary>
public static class GeneratorReferences {

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences =
        new(BuildPlatformReferences);

    /// <summary>The base class library, and nothing else.</summary>
    public static ImmutableArray<MetadataReference> Platform => PlatformReferences.Value;

    /// <summary>
    /// The base class library plus the assemblies containing <paramref name="anchorTypes"/> and
    /// everything those assemblies reference.
    /// </summary>
    public static ImmutableArray<MetadataReference> For(IReadOnlyList<Type> anchorTypes) {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in Platform) {
            builder.Add(reference);

            if (reference.Display is { } display) {
                seen.Add(Path.GetFileNameWithoutExtension(display));
            }
        }

        foreach (var assembly in ExpandTransitively(anchorTypes.Select(type => type.Assembly))) {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) {
                continue;
            }

            if (seen.Add(Path.GetFileNameWithoutExtension(assembly.Location))) {
                builder.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        return builder.ToImmutable();
    }

    /// <inheritdoc cref="For(System.Collections.Generic.IReadOnlyList{System.Type})"/>
    public static ImmutableArray<MetadataReference> For(params Type[] anchorTypes) =>
        For((IReadOnlyList<Type>)anchorTypes);

    /// <summary>
    /// Walks assembly references breadth-first. Anchoring on <c>Hardened.Web.Runtime</c> has to pull
    /// in <c>Hardened.Requests.Abstract</c> too, or the source under test does not compile and the
    /// failure reads as a generator bug rather than a missing reference.
    /// </summary>
    private static IEnumerable<Assembly> ExpandTransitively(IEnumerable<Assembly> roots) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();

        foreach (var root in roots) {
            queue.Enqueue(root);
        }

        while (queue.Count > 0) {
            var assembly = queue.Dequeue();

            if (assembly.FullName is not { } name || !seen.Add(name)) {
                continue;
            }

            yield return assembly;

            foreach (var referenced in assembly.GetReferencedAssemblies()) {
                Assembly loaded;

                try {
                    loaded = Assembly.Load(referenced);
                }
                catch (Exception) {
                    // A reference that cannot be resolved is not necessarily needed by the source
                    // under test. If it is, the compiler says so far more clearly than anything
                    // that could be thrown from here.
                    continue;
                }

                queue.Enqueue(loaded);
            }
        }
    }

    private static ImmutableArray<MetadataReference> BuildPlatformReferences() {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trusted) {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unavailable, so the base class library cannot be " +
                "resolved. This library targets .NET Core and above.");
        }

        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
