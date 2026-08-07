using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// Header access for generated request binding code.
///
/// IExecutionRequest exposes PathTokens and QueryString as collections that carry their own
/// Get, but Headers is a plain IDictionary. Generated [FromHeader] binding calls Get on all
/// three uniformly, so this supplies the missing one with the same semantics: a miss yields
/// <see cref="StringValues.Empty"/> rather than throwing, exactly as
/// IQueryStringCollection.Get does.
///
/// This namespace is already imported by generated handlers, which is why the extension
/// lives here rather than alongside the header abstractions.
/// </summary>
public static class HeaderDictionaryExtensions {

    public static StringValues Get(this IDictionary<string, StringValues> headers, string key) {
        return headers.TryGetValue(key, out var value) ? value : StringValues.Empty;
    }
}
