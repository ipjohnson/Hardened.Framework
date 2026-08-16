namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Joins a base path to a route template.
/// </summary>
/// <remarks>
/// <para>
/// Five places compose these two strings — the route tree, the ambiguity diagnostic, the generated
/// links, the served OpenAPI document, and the class-level <c>[BasePath]</c> in the handler model.
/// They have to agree, because between them they decide what the matcher answers, what the build
/// rejects, what a link builds and what a client is told exists. All five used to concatenate,
/// which agreed only because they were all wrong the same way.
/// </para>
/// <para>
/// Two rules, and the first is the one that was missing:
/// </para>
/// <list type="number">
/// <item>
/// <b>A template of <c>/</c> under a base path contributes nothing.</b>
/// <c>[BasePath("/collection")]</c> with <c>[Get("/")]</c> means "the collection lives at the root
/// of my space", and the root of <c>/collection</c> is <c>/collection</c>. Concatenating produced
/// <c>/collection/</c>, so the URL the controller declared answered nothing — and since trailing
/// slashes are significant and the default policy is strict, the other spelling was not one a
/// client could be expected to guess. With no base path, <c>[Get("/")]</c> is still the root
/// <c>/</c>, which is the same rule: <c>/</c> is the identity of a path, which is also why
/// <c>WebExecutionHandlerService.Alternative</c> says the root has no other spelling.
/// </item>
/// <item>
/// <b>The boundary slash is collapsed, never doubled.</b> A base path written with a trailing
/// slash used to produce <c>//</c> in the middle of every route under it.
/// </item>
/// </list>
/// <para>
/// A trailing slash the author wrote on a longer template is kept — <c>[Get("/items/")]</c> under
/// <c>/collection</c> is <c>/collection/items/</c>. That is a deliberate choice about a real URL,
/// where <c>/</c> alone is the absence of one.
/// </para>
/// </remarks>
public static class RoutePath {

    public static string Combine(string? basePath, string? template) {
        var start = basePath ?? "";
        var rest = template ?? "";

        // The base is the whole answer: no template, or one that only names the base's own root.
        if (rest.Length == 0 || rest == "/") {
            var root = start.TrimEnd('/');

            return root.Length == 0 ? "/" : root;
        }

        if (start.Length == 0) {
            return rest[0] == '/' ? rest : "/" + rest;
        }

        return start.TrimEnd('/') + "/" + rest.TrimStart('/');
    }
}
