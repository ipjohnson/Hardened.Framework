using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The registry one registration pass writes into.
/// </summary>
/// <remarks>
/// One-shot: it builds a table, and the table is immutable from the moment startup completes.
/// Registering after that throws and names the route, which is Fastify's model and the one
/// decided - a table that never changes needs no synchronisation on the read path and no defence
/// against a request observing a half-applied change.
/// </remarks>
public sealed class RouteRegistry : IRouteRegistry
{
    private readonly RuntimeRouteTableBuilder _builder;
    private readonly IGeneratedRouteHandlerCatalog? _catalog;
    private readonly List<string> _failures = new();
    private readonly List<(string Path, string Operation)> _operations = new();
    private readonly string _basePath;

    private bool _closed;

    public RouteRegistry(IServiceProvider serviceProvider, IGeneratedRouteHandlerCatalog? catalog)
    {
        ServiceProvider = serviceProvider;
        _catalog = catalog;
        _basePath = catalog?.BasePath ?? "";
        _builder = new RuntimeRouteTableBuilder(
            catalog?.CaseInsensitiveRoutes ?? false,
            catalog?.Constraints
        );
    }

    public IServiceProvider ServiceProvider { get; }

    public IReadOnlyList<string> Failures => _failures;

    /// <summary>
    /// What each registered route publishes, and at which path. Read once when registration closes,
    /// to write the served document.
    /// </summary>
    public IReadOnlyList<(string Path, string Operation)> Operations => _operations;

    public IRouteRegistry Get(string path, Type controllerType, string handlerMethod) =>
        Map("GET", path, controllerType, handlerMethod);

    public IRouteRegistry Post(string path, Type controllerType, string handlerMethod) =>
        Map("POST", path, controllerType, handlerMethod);

    public IRouteRegistry Put(string path, Type controllerType, string handlerMethod) =>
        Map("PUT", path, controllerType, handlerMethod);

    public IRouteRegistry Patch(string path, Type controllerType, string handlerMethod) =>
        Map("PATCH", path, controllerType, handlerMethod);

    public IRouteRegistry Delete(string path, Type controllerType, string handlerMethod) =>
        Map("DELETE", path, controllerType, handlerMethod);

    public IRouteRegistry Map(string method, string path, Type controllerType, string handlerMethod)
    {
        Closed(path);

        var verb = (method ?? "").ToUpperInvariant();
        var handler = Find(verb, path, controllerType, handlerMethod);

        if (handler == null)
        {
            return this;
        }

        var composed = Compose(path);

        if (!BindsTheSameTokens(handler, composed, path))
        {
            return this;
        }

        if (
            !_builder.TryAdd(
                composed,
                verb,
                routePath => handler.Factory(ServiceProvider, routePath),
                out var error
            )
        )
        {
            _failures.Add(error!);

            return this;
        }

        Publishes(composed, handler.Operation);

        return this;
    }

    public IRouteRegistry Get(string path, Delegate handler) => Map("GET", path, handler);

    public IRouteRegistry Post(string path, Delegate handler) => Map("POST", path, handler);

    public IRouteRegistry Put(string path, Delegate handler) => Map("PUT", path, handler);

    public IRouteRegistry Patch(string path, Delegate handler) => Map("PATCH", path, handler);

    public IRouteRegistry Delete(string path, Delegate handler) => Map("DELETE", path, handler);

    /// <inheritdoc />
    /// <remarks>
    /// Throws, and that is the whole implementation. The generator rewrites every call it sees to
    /// an overload that carries the emitted handler, so reaching this one means the generator did
    /// not see the call site - and a route that binds nothing is worse than a startup failure that
    /// says so.
    /// </remarks>
    public IRouteRegistry Map(string method, string path, Delegate handler) =>
        throw new InvalidOperationException(
            $"'{path}' was registered with a lambda the build did not read, so there is no handler for it. "
                + "A lambda registration has to be written directly in a method the Hardened web generator compiles - "
                + "it cannot be passed through a helper, built into a variable, or written in a project the generator does not run on."
        );

    /// <inheritdoc />
    public IRouteRegistry Map(string path, RegisteredRouteHandler handler)
    {
        Closed(path);

        var composed = Compose(path);

        if (!DeclaresTokens(handler.BoundTokens, composed, path, "the registered handler"))
        {
            return this;
        }

        if (
            !_builder.TryAdd(
                composed,
                handler.Method,
                routePath => handler.Factory(ServiceProvider, routePath),
                out var error
            )
        )
        {
            _failures.Add(error!);

            return this;
        }

        Publishes(composed, handler.Operation);

        return this;
    }

    /// <summary>The table as registered. Closes the registry.</summary>
    public RuntimeRouteTable Close()
    {
        _closed = true;

        if (_failures.Count > 0)
        {
            throw new RouteRegistrationException(_failures);
        }

        return _builder.Build();
    }

    /// <summary>
    /// Records what a route that did register publishes.
    /// </summary>
    /// <remarks>
    /// Only a route that registered. A failed one is in the document of no application, because a
    /// failed registration fails startup.
    /// </remarks>
    private void Publishes(string path, string operation)
    {
        if (operation.Length > 0)
        {
            _operations.Add((RouteTemplateParser.NamesOnly(path), operation));
        }
    }

    private void Closed(string path)
    {
        if (_closed)
        {
            throw new InvalidOperationException(
                $"Routes cannot be registered after startup, and '{path}' was. The table is built once and is immutable from then on."
            );
        }
    }

    /// <remarks>
    /// The entry point's base path, composed the way it is composed onto an attribute route. A
    /// trailing slash on either side would otherwise double up.
    /// </remarks>
    private string Compose(string path)
    {
        if (_basePath.Length == 0 || path == null)
        {
            return path ?? "";
        }

        var trimmed = _basePath.TrimEnd('/');

        if (trimmed.Length == 0)
        {
            return path;
        }

        return path.Length > 0 && path[0] == '/' ? trimmed + path : trimmed + "/" + path;
    }

    private GeneratedRouteHandler? Find(
        string verb,
        string path,
        Type controllerType,
        string handlerMethod
    )
    {
        if (_catalog == null)
        {
            _failures.Add(
                $"'{path}' names {Describe(controllerType, handlerMethod)}, and this application generated no handler catalog to find it in - the catalog is emitted only where the compilation declares an {nameof(IRouteRegistration)}"
            );

            return null;
        }

        GeneratedRouteHandler? otherVerb = null;

        foreach (var candidate in _catalog.Handlers)
        {
            if (
                candidate.ControllerType != controllerType
                || !string.Equals(candidate.HandlerMethod, handlerMethod, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (string.Equals(candidate.Method, verb, StringComparison.Ordinal))
            {
                return candidate;
            }

            otherVerb = candidate;
        }

        _failures.Add(
            otherVerb == null
                ? $"'{path}' names {Describe(controllerType, handlerMethod)}, which is not a handler - it needs a verb attribute for the generator to emit one"
                : $"'{path}' registers {Describe(controllerType, handlerMethod)} under {verb}, and it is declared {otherVerb.Method}"
        );

        return null;
    }

    /// <summary>
    /// Whether every token the handler was compiled to read is declared by the template it is being
    /// registered under.
    /// </summary>
    /// <remarks>
    /// The check §8 calls the one most likely to bite. A generated binder does not read token 0, it
    /// reads the token called <c>id</c> - which is what lets a handler compiled for
    /// <c>/items/{id}</c> bind correctly against <c>/tenant-7/items/{id}</c> without being told.
    /// Registered under a template that spells it <c>orderId</c>, the same handler binds nothing and
    /// answers 400 to every request.
    /// </remarks>
    private bool BindsTheSameTokens(
        GeneratedRouteHandler handler,
        string composed,
        string asWritten
    )
    {
        if (!RouteTemplateParser.TryParse(handler.DeclaredPath, out var declared, out _))
        {
            // The declared path came from an attribute the generator already accepted, so this is
            // unreachable short of a generator defect. Reported rather than assumed away.
            _failures.Add(
                $"'{handler.DeclaredPath}', which {Describe(handler.ControllerType, handler.HandlerMethod)} was declared with, is not a route template"
            );

            return false;
        }

        return DeclaresTokens(
            RouteTemplateParser.TokenNames(declared),
            composed,
            asWritten,
            $"{Describe(handler.ControllerType, handler.HandlerMethod)} reads - it was declared as '{handler.DeclaredPath}'"
        );
    }

    /// <summary>
    /// Whether <paramref name="composed"/> declares every token in <paramref name="required"/>.
    /// </summary>
    private bool DeclaresTokens(
        IReadOnlyList<string> required,
        string composed,
        string asWritten,
        string reader
    )
    {
        if (required.Count == 0)
        {
            return true;
        }

        if (!RouteTemplateParser.TryParse(composed, out var registered, out var error))
        {
            _failures.Add(error!);

            return false;
        }

        var names = RouteTemplateParser.TokenNames(registered);

        foreach (var name in required)
        {
            if (Array.IndexOf(names, name) >= 0)
            {
                continue;
            }

            _failures.Add($"'{asWritten}' does not declare the token '{{{name}}}', which {reader}");

            return false;
        }

        return true;
    }

    private static string Describe(Type controllerType, string handlerMethod) =>
        controllerType.Name + "." + handlerMethod;
}
