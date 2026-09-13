using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Builds a <see cref="RuntimeRouteTable"/> one route at a time, then freezes it.
/// </summary>
/// <remarks>
/// <para>
/// Every failure is reported rather than thrown, and the caller decides when to give up. A registry
/// that threw on the first bad route would make a fifty-route registration a fifty-restart
/// debugging session.
/// </para>
/// <para>
/// Not thread safe. One registration pass builds one table, and the table it produces is immutable.
/// </para>
/// </remarks>
public sealed class RuntimeRouteTableBuilder
{
    private readonly MutableNode _root;
    private readonly bool _caseInsensitive;
    private readonly IReadOnlyDictionary<string, RouteConstraintTest>? _constraints;

    private int _maximumTokens;
    private int _count;

    /// <param name="caseInsensitive">
    /// Matches literal segments without regard to case, from <c>[CaseInsensitiveRoutes]</c> on the
    /// entry point - the same flag, from the same place, as the generated table compiles in.
    /// </param>
    /// <param name="constraints">
    /// The <c>[RouteConstraint]</c> methods the application declared, by the name a template uses
    /// after the colon. A template naming one that is not here fails to register.
    /// </param>
    public RuntimeRouteTableBuilder(
        bool caseInsensitive = false,
        IReadOnlyDictionary<string, RouteConstraintTest>? constraints = null
    )
    {
        _caseInsensitive = caseInsensitive;
        _constraints = constraints;
        _root = new MutableNode(caseInsensitive);
    }

    /// <summary>
    /// Adds one route, or says why it is not one.
    /// </summary>
    /// <param name="template">The route template, in the syntax an attribute route uses.</param>
    /// <param name="method">The verb, which is upper-cased here.</param>
    /// <param name="handlerFactory">
    /// Builds the handler that answers, given the template it answers at. The path is passed in
    /// rather than captured so that a generated handler can be constructed with it - what
    /// <c>ExecutionRequestHandlerInfo.WithPath</c> exists for, and what makes the handler report the
    /// route it is actually served at.
    /// </param>
    public bool TryAdd(
        string template,
        string method,
        Func<string, IExecutionRequestHandler> handlerFactory,
        out string? error
    )
    {
        if (!RouteTemplateParser.TryParse(template, out var segments, out error))
        {
            return false;
        }

        if (string.IsNullOrEmpty(method))
        {
            error = $"'{template}' was registered with no verb";

            return false;
        }

        var verb = method.ToUpperInvariant();
        var node = _root;
        var tokens = 0;
        MutableEndpoints? endpoints = null;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];

            if (segment.Kind == RouteSegmentKind.Literal)
            {
                node = node.Literal(segment.Value);

                continue;
            }

            if (
                !RouteConstraintChain.TryResolve(
                    segment.Constraint,
                    _constraints,
                    out var tests,
                    out var rank,
                    out error
                )
            )
            {
                error = $"'{template}' is not routable: {error}";

                return false;
            }

            tokens++;

            if (segment.Kind == RouteSegmentKind.CatchAll)
            {
                endpoints = node.CatchAll(segment.Constraint ?? "", tests, rank);

                break;
            }

            node = node.Token(segment.Constraint ?? "", tests, rank);
        }

        endpoints ??= node.Endpoints();

        if (endpoints.Declares(verb, out var existing))
        {
            error =
                $"'{template}' answers {verb} at the same path as '{existing}', so one of them could never be reached";

            return false;
        }

        endpoints.Add(
            new RouteEntry(verb, template, RouteTemplateParser.TokenNames(segments), handlerFactory)
        );

        if (tokens > _maximumTokens)
        {
            _maximumTokens = tokens;
        }

        _count++;

        return true;
    }

    /// <summary>The table as registered, immutable from here on.</summary>
    public RuntimeRouteTable Build()
    {
        var literalPaths = new Dictionary<string, RouteEndpoints>(
            _caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );

        var root = _root.Freeze("", literalPaths);

        return new RuntimeRouteTable(
            root,
            new SegmentLookup<RouteEndpoints>(literalPaths, _caseInsensitive),
            _maximumTokens,
            _count
        );
    }

    private sealed class MutableNode
    {
        private readonly bool _caseInsensitive;
        private readonly Dictionary<string, MutableNode> _literals;
        private readonly Dictionary<string, MutableEdge> _tokens = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MutableEdge> _catchAlls = new(StringComparer.Ordinal);

        private MutableEndpoints? _endpoints;

        public MutableNode(bool caseInsensitive)
        {
            _caseInsensitive = caseInsensitive;
            _literals = new Dictionary<string, MutableNode>(
                caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
            );
        }

        public MutableNode Literal(string text)
        {
            if (!_literals.TryGetValue(text, out var child))
            {
                child = new MutableNode(_caseInsensitive);
                _literals.Add(text, child);
            }

            return child;
        }

        /// <remarks>
        /// Keyed by the constraint chain as written, so <c>{id:int}</c> and <c>{slug}</c> at the
        /// same position are two alternatives with two continuations, while <c>{id:int}</c> and
        /// <c>{productId:int}</c> are one. That is the shape the generated tree has for the same
        /// routes, where a node is shared and the matched leaf supplies the token's name.
        /// </remarks>
        public MutableNode Token(string chain, RouteConstraintTest[] tests, int rank)
        {
            if (!_tokens.TryGetValue(chain, out var edge))
            {
                edge = new MutableEdge(tests, rank, new MutableNode(_caseInsensitive));
                _tokens.Add(chain, edge);
            }

            return edge.Child;
        }

        public MutableEndpoints CatchAll(string chain, RouteConstraintTest[] tests, int rank)
        {
            if (!_catchAlls.TryGetValue(chain, out var edge))
            {
                edge = new MutableEdge(tests, rank, new MutableNode(_caseInsensitive));
                _catchAlls.Add(chain, edge);
            }

            return edge.Child.Endpoints();
        }

        public MutableEndpoints Endpoints() => _endpoints ??= new MutableEndpoints();

        /// <param name="path">
        /// The literal path reaching this node, or null once a token has been crossed. A node no
        /// token leads to answers one whole path, and that path goes in the table checked before the
        /// walk.
        /// </param>
        public RouteNode Freeze(string? path, Dictionary<string, RouteEndpoints> literalPaths)
        {
            var literals = new Dictionary<string, RouteNode>(
                _caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
            );

            foreach (var literal in _literals)
            {
                literals.Add(
                    literal.Key,
                    literal.Value.Freeze(
                        path == null ? null : path + "/" + literal.Key,
                        literalPaths
                    )
                );
            }

            var endpoints = _endpoints?.Freeze();

            if (endpoints != null && path != null)
            {
                // The root is "" here and answers "/", which is the one path the walk spells
                // differently from the way it is registered.
                literalPaths[path.Length == 0 ? "/" : path] = endpoints;
            }

            return new RouteNode(
                new SegmentLookup<RouteNode>(literals, _caseInsensitive),
                Ordered(
                    _tokens,
                    edge => new TokenEdge(edge.Tests, edge.Child.Freeze(null, literalPaths))
                ),
                Ordered(
                    _catchAlls,
                    edge => new CatchAllEdge(edge.Tests, edge.Child.Endpoints().Freeze())
                ),
                endpoints
            );
        }

        /// <summary>
        /// The alternatives at one position, narrowest first.
        /// </summary>
        /// <remarks>
        /// By the published precedence numbers, then by the chain as written so that two constraints
        /// of equal rank order the same way on every build. An unconstrained token sorts last,
        /// because it matches everything the constrained ones do.
        /// </remarks>
        private static TEdge[] Ordered<TEdge>(
            Dictionary<string, MutableEdge> edges,
            Func<MutableEdge, TEdge> freeze
        )
        {
            if (edges.Count == 0)
            {
                return Array.Empty<TEdge>();
            }

            var ordered = new List<KeyValuePair<string, MutableEdge>>(edges);

            ordered.Sort(
                (left, right) =>
                    left.Value.Rank != right.Value.Rank
                        ? left.Value.Rank.CompareTo(right.Value.Rank)
                        : string.CompareOrdinal(left.Key, right.Key)
            );

            var frozen = new TEdge[ordered.Count];

            for (var index = 0; index < ordered.Count; index++)
            {
                frozen[index] = freeze(ordered[index].Value);
            }

            return frozen;
        }
    }

    private sealed class MutableEdge
    {
        public MutableEdge(RouteConstraintTest[] tests, int rank, MutableNode child)
        {
            Tests = tests;
            Rank = rank;
            Child = child;
        }

        public RouteConstraintTest[] Tests { get; }

        public int Rank { get; }

        public MutableNode Child { get; }
    }

    private sealed class MutableEndpoints
    {
        private readonly List<RouteEntry> _entries = new();
        private readonly Dictionary<string, string> _declaredBy = new(StringComparer.Ordinal);

        public bool Declares(string method, out string template)
        {
            if (_declaredBy.TryGetValue(method, out var declared))
            {
                template = declared;

                return true;
            }

            template = "";

            return false;
        }

        public void Add(RouteEntry entry)
        {
            _entries.Add(entry);
            _declaredBy[entry.Method] = entry.Template;
        }

        /// <remarks>
        /// A HEAD is a GET whose body is dropped on the way out, so a GET route answers both. The
        /// generated table emits <c>case "HEAD": case "GET":</c> for exactly this, and
        /// <c>WebExecutionHandlerService</c> runs the chain in full and drops the body. An
        /// explicitly registered HEAD wins, because it was asked for.
        /// </remarks>
        public RouteEndpoints Freeze()
        {
            var entries = new List<RouteEntry>(_entries);

            if (!_declaredBy.ContainsKey("HEAD"))
            {
                foreach (var entry in _entries)
                {
                    if (string.Equals(entry.Method, "GET", StringComparison.Ordinal))
                    {
                        entries.Add(new RouteEntry("HEAD", entry));

                        break;
                    }
                }
            }

            var allow = new System.Text.StringBuilder();

            foreach (var entry in entries)
            {
                if (allow.Length > 0)
                {
                    allow.Append(", ");
                }

                allow.Append(entry.Method);
            }

            return new RouteEndpoints(entries.ToArray(), allow.ToString());
        }
    }
}
