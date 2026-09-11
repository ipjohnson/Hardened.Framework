using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The media types this compilation can write a model as, read from
/// <c>[assembly: WritesContentType]</c> on itself and on everything it references.
/// </summary>
/// <remarks>
/// <para>
/// <c>HRDR012</c>'s other half. The transform decides which of an operation's declared media types
/// are candidates, knowing only the return type; this decides which of those a serializer exists
/// for, knowing only the compilation. Neither can answer alone, which is why the finding is carried
/// from one to the other rather than reported where it is found.
/// </para>
/// <para>
/// <b>Referenced assemblies as well as this one.</b> A serializer usually arrives as a package, so
/// the attribute usually sits in metadata rather than in source. Reading only the current assembly
/// would leave the warning exactly as wrong as it was.
/// </para>
/// <para>
/// <b>Selected to one string.</b> A <c>CompilationProvider</c> hands back a new compilation on every
/// keystroke, so anything derived from it has to collapse to a value that compares equal or it
/// rebuilds the routing table for every edit. Sorted and joined for that reason, and for the same
/// reason <c>WebGeneratorOptions</c> holds raw strings:
/// <c>HandlerValidationGenerator</c> makes the identical move with a bool.
/// </para>
/// </remarks>
public static class SerializerContentTypes {
    private const string AttributeName = "Hardened.Requests.Abstract.Attributes.WritesContentTypeAttribute";

    /// <summary>
    /// <c>application/json</c> is always here. The framework registers a serializer for it, and an
    /// application replacing that one replaces it with another declaring the same media type, so no
    /// reference has to say so.
    /// </summary>
    public const string AlwaysWritable = "application/json";

    public static string Read(Compilation compilation) {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { AlwaysWritable };

        Collect(compilation.Assembly, found);

        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols) {
            Collect(reference, found);
        }

        return string.Join(",", found);
    }

    /// <summary>
    /// Whether <paramref name="contentType"/> is one of the media types <paramref name="writable"/>
    /// names.
    /// </summary>
    /// <remarks>
    /// An exact comparison, because that is what the runtime does:
    /// <c>SerializationLocatorService.ProducerOf</c> is a dictionary lookup on the serializer's own
    /// tag, and it is the lookup the compile-time binding uses. A wildcard match here would go
    /// quiet for a spelling the locator will not find.
    /// </remarks>
    public static bool Writes(string writable, string contentType) {
        foreach (var declared in writable.Split(',')) {
            if (declared.Equals(contentType, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static void Collect(IAssemblySymbol assembly, ISet<string> found) {
        foreach (var attribute in assembly.GetAttributes()) {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeName) {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1) {
                continue;
            }

            foreach (var value in attribute.ConstructorArguments[0].Values) {
                if (value.Value is string contentType && contentType.Length > 0) {
                    found.Add(contentType);
                }
            }
        }
    }
}
