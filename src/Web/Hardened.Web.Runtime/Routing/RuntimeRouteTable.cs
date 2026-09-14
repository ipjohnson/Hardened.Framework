using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// A routing table that is data rather than emitted code.
/// </summary>
/// <remarks>
/// <para>
/// The generated table is a character trie unrolled into methods, which is the fastest thing a
/// route table can be and needs every path to be a literal the compiler can see. This is the same
/// table read one segment at a time instead of one character at a time, so a path that only exists
/// at run time can still be routed. It answers the same
/// <see cref="IWebExecutionRequestHandlerProvider"/> contract, which is what makes everything below
/// the match - the filter chain, authorization, serialization, the response cache, logging - shared
/// between the two rather than reimplemented for one of them.
/// </para>
/// <para>
/// <b>Immutable once built.</b> Registration closes when startup completes, so a match needs no
/// synchronisation, no volatile read and no defence against a half-applied change. See
/// <c>RouteRegistryStartupService</c> for where it closes.
/// </para>
/// <para>
/// <b>Allocation.</b> A match allocates the <see cref="PathTokenCollection"/> the request needs and
/// one string per bound token, which is what the generated table allocates for the same route, and
/// nothing else. The token values found on the way down are held in a stack buffer, and segments
/// are looked up by span - see <see cref="SegmentLookup{TValue}"/>.
/// </para>
/// </remarks>
public sealed class RuntimeRouteTable
{
    private readonly RouteNode _root;
    private readonly SegmentLookup<RouteEndpoints> _literalPaths;
    private readonly int _maximumTokens;

    internal RuntimeRouteTable(
        RouteNode root,
        SegmentLookup<RouteEndpoints> literalPaths,
        int maximumTokens,
        int count
    )
    {
        _root = root;
        _literalPaths = literalPaths;
        _maximumTokens = maximumTokens;
        Count = count;
    }

    /// <summary>A table with no routes, which every request falls straight through.</summary>
    public static readonly RuntimeRouteTable Empty = new(
        RouteNode.Empty,
        SegmentLookup<RouteEndpoints>.Empty,
        0,
        0
    );

    /// <summary>How many routes were registered, verbs counted separately.</summary>
    public int Count { get; }

    /// <summary>
    /// What answers <paramref name="method"/> at <paramref name="path"/>: a handler, a 405 naming
    /// what the path does answer, or null where no route matched at all.
    /// </summary>
    public RequestHandlerInfo? Match(ReadOnlySpan<char> path, string method)
    {
        if (Count == 0 || path.Length == 0 || path[0] != '/')
        {
            return null;
        }

        // Every route with no token at all, in one probe. A dynamic registration is most often a
        // list of literal paths - a tenant slug, a feature flag - and this answers all of those
        // without walking anything.
        if (_literalPaths.TryGetValue(path, out var literal))
        {
            return literal.Resolve(method, path, default, 0);
        }

        Span<TokenSlice> tokens =
            _maximumTokens == 0 ? default : stackalloc TokenSlice[_maximumTokens];

        return Walk(_root, path, 1, method, tokens, 0);
    }

    /// <remarks>
    /// <para>
    /// Recursive because a failed match has to unwind: <c>/items/{id:int}</c> and
    /// <c>/items/{name:slug}</c> share a position, and a path that fails the first has to be offered
    /// the second. Depth is bounded by the segment count of the longest registered route.
    /// </para>
    /// <para>
    /// A verb miss does not unwind. The leaf answers 405, which is not null, and that propagates -
    /// the same thing the generated table does, where a leaf switch falling to <c>default</c>
    /// returns the shared <c>MethodNotAllowed</c> rather than null.
    /// </para>
    /// </remarks>
    private RequestHandlerInfo? Walk(
        RouteNode node,
        ReadOnlySpan<char> path,
        int index,
        string method,
        Span<TokenSlice> tokens,
        int depth
    )
    {
        var rest = path.Slice(index);
        var slash = rest.IndexOf('/');
        var end = slash < 0 ? path.Length : index + slash;
        var segment = path.Slice(index, end - index);
        var last = slash < 0;

        if (node.Literals.TryGetValue(segment, out var child))
        {
            var matched = last
                ? child.Endpoints?.Resolve(method, path, tokens, depth)
                : Walk(child, path, end + 1, method, tokens, depth);

            if (matched != null)
            {
                return matched;
            }
        }

        // A token names at least one character. Nothing is left to name when the path ended on the
        // separator, so /collection/ is not a match for /collection/{id} - it used to bind the token
        // to "" and answer 400 from the binder, about a URL that addresses no endpoint at all.
        if (segment.Length > 0)
        {
            foreach (var edge in node.Tokens)
            {
                // A value that fails the constraint is not a match at all, so the walk is free to
                // try the next alternative. That is what makes {id:int} a 404 rather than a 400.
                if (!RouteConstraintChain.Passes(edge.Tests, segment))
                {
                    continue;
                }

                tokens[depth] = new TokenSlice(index, segment.Length);

                var matched = last
                    ? edge.Child.Endpoints?.Resolve(method, path, tokens, depth + 1)
                    : Walk(edge.Child, path, end + 1, method, tokens, depth + 1);

                if (matched != null)
                {
                    return matched;
                }
            }
        }

        // The catch-all takes the rest of the path, separators included, and has to take at least
        // one character for the reason above: /assets/ has no rest.
        if (rest.Length > 0)
        {
            foreach (var edge in node.CatchAlls)
            {
                if (!RouteConstraintChain.Passes(edge.Tests, rest))
                {
                    continue;
                }

                tokens[depth] = new TokenSlice(index, rest.Length);

                return edge.Endpoints.Resolve(method, path, tokens, depth + 1);
            }
        }

        return null;
    }
}

/// <summary>Where one bound token's value sits in the request path.</summary>
internal readonly struct TokenSlice
{
    public TokenSlice(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }
}

/// <summary>One position in the segment trie.</summary>
internal sealed class RouteNode
{
    public RouteNode(
        SegmentLookup<RouteNode> literals,
        TokenEdge[] tokens,
        CatchAllEdge[] catchAlls,
        RouteEndpoints? endpoints
    )
    {
        Literals = literals;
        Tokens = tokens;
        CatchAlls = catchAlls;
        Endpoints = endpoints;
    }

    public static readonly RouteNode Empty = new(
        SegmentLookup<RouteNode>.Empty,
        Array.Empty<TokenEdge>(),
        Array.Empty<CatchAllEdge>(),
        null
    );

    public SegmentLookup<RouteNode> Literals { get; }

    /// <summary>
    /// The token alternatives at this position, narrowest first.
    /// </summary>
    /// <remarks>
    /// An array rather than a list, and walked with <c>foreach</c> over the array type, so the
    /// compiler emits an indexed walk rather than an interface call per element - the reason
    /// <c>WebExecutionHandlerService._handlers</c> gives. There is rarely more than one.
    /// </remarks>
    public TokenEdge[] Tokens { get; }

    public CatchAllEdge[] CatchAlls { get; }

    /// <summary>The routes that end here, or null where this position is only passed through.</summary>
    public RouteEndpoints? Endpoints { get; }
}

/// <summary>One token alternative at a position, and what follows it.</summary>
internal sealed class TokenEdge
{
    public TokenEdge(RouteConstraintTest[] tests, RouteNode child)
    {
        Tests = tests;
        Child = child;
    }

    public RouteConstraintTest[] Tests { get; }

    public RouteNode Child { get; }
}

/// <summary>A catch-all, which ends the route it is written on.</summary>
internal sealed class CatchAllEdge
{
    public CatchAllEdge(RouteConstraintTest[] tests, RouteEndpoints endpoints)
    {
        Tests = tests;
        Endpoints = endpoints;
    }

    public RouteConstraintTest[] Tests { get; }

    public RouteEndpoints Endpoints { get; }
}

/// <summary>The routes declared at one path, by verb.</summary>
internal sealed class RouteEndpoints
{
    private readonly RouteEntry[] _entries;
    private readonly RequestHandlerInfo _methodNotAllowed;

    public RouteEndpoints(RouteEntry[] entries, string allow)
    {
        _entries = entries;

        // Built once here rather than per request, because it carries nothing per request. The
        // generated table holds the same value in a static field per distinct verb set.
        _methodNotAllowed = RequestHandlerInfo.MethodNotAllowed(allow);
    }

    public RequestHandlerInfo Resolve(
        string method,
        ReadOnlySpan<char> path,
        ReadOnlySpan<TokenSlice> tokens,
        int count
    )
    {
        foreach (var entry in _entries)
        {
            // Ordinal. A method is case-sensitive in RFC 9110, and the generated table switches on
            // the string, which is the same comparison.
            if (!string.Equals(entry.Method, method, StringComparison.Ordinal))
            {
                continue;
            }

            return new RequestHandlerInfo(
                entry.Handler,
                Bind(entry.TokenNames, path, tokens, count)
            );
        }

        return _methodNotAllowed;
    }

    /// <remarks>
    /// The names come from the route that matched and the values were found on the way down, which
    /// is the split <see cref="PathTokenCollection"/> exists for: a node shared by two routes cannot
    /// know which name applies at a position, so the leaf supplies the names and the values are
    /// filled in positionally.
    /// </remarks>
    private static PathTokenCollection Bind(
        string[] names,
        ReadOnlySpan<char> path,
        ReadOnlySpan<TokenSlice> tokens,
        int count
    )
    {
        if (names.Length == 0)
        {
            return PathTokenCollection.Empty;
        }

        var collection = new PathTokenCollection(names.Length, names);

        for (var index = 0; index < names.Length && index < count; index++)
        {
            var slice = tokens[index];

            collection.SetValue(index, path.Slice(slice.Start, slice.Length).ToString());
        }

        return collection;
    }
}

/// <summary>One route: a verb, the names it binds, and the handler that answers it.</summary>
internal sealed class RouteEntry
{
    private readonly HandlerSlot _slot;

    public RouteEntry(
        string method,
        string template,
        string[] tokenNames,
        Func<string, IExecutionRequestHandler> factory
    )
    {
        Method = method;
        Template = template;
        TokenNames = tokenNames;
        _slot = new HandlerSlot(template, factory);
    }

    /// <summary>
    /// The same route under another verb, answered by the same handler instance.
    /// </summary>
    /// <remarks>
    /// What a HEAD is: the GET handler, run in full, with the body dropped on the way out. Sharing
    /// the slot rather than the factory is what keeps it one handler - two would mean two filter
    /// chains, and a filter holding per-handler state would hold two copies of it.
    /// </remarks>
    public RouteEntry(string method, RouteEntry source)
    {
        Method = method;
        Template = source.Template;
        TokenNames = source.TokenNames;
        _slot = source._slot;
    }

    public string Method { get; }

    /// <summary>The template as registered, for the message a duplicate route produces.</summary>
    public string Template { get; }

    public string[] TokenNames { get; }

    public IExecutionRequestHandler Handler => _slot.Handler;

    /// <remarks>
    /// Built on the first request that reaches this route rather than at registration, which is what
    /// the generated table does with <c>??=</c>. Registration runs on the init path of a cold start,
    /// and a route nobody calls should not cost a handler, a filter chain and whatever its
    /// dependencies pull in.
    /// </remarks>
    private sealed class HandlerSlot
    {
        private readonly Func<string, IExecutionRequestHandler> _factory;
        private readonly string _template;
        private IExecutionRequestHandler? _handler;

        public HandlerSlot(string template, Func<string, IExecutionRequestHandler> factory)
        {
            _template = template;
            _factory = factory;
        }

        public IExecutionRequestHandler Handler => _handler ??= _factory(_template);
    }
}
