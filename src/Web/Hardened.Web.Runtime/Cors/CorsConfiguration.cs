namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Which origins may call this application, and what they may see when they do.
/// </summary>
public class CorsConfiguration {
    public const string DefaultEnvironmentVariable = "CORS_ALLOWED_ORIGINS";

    private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _allowedOriginSuffixes = new();
    private readonly HashSet<string> _allowedHeaders =
        new(StringComparer.OrdinalIgnoreCase) {
            "Authorization", "Content-Type", "Accept", "x-auth-token", "x-amz-content-sha256"
        };
    private readonly List<string> _exposedHeaders = new();

    public string EnvironmentVariable { get; set; } = DefaultEnvironmentVariable;

    public IReadOnlySet<string> AllowedOrigins => _allowedOrigins;

    public IReadOnlyCollection<string> AllowedHeaders => _allowedHeaders;

    public IReadOnlyCollection<string> ExposedHeaders => _exposedHeaders;

    /// <summary>
    /// Answer every origin. Off, and worth leaving off: it cannot be combined with
    /// <see cref="AllowCredentials"/>, and it makes every future endpoint public by default.
    /// </summary>
    public bool AllowAnyOrigin { get; set; }

    /// <summary>
    /// Let the browser send cookies and <c>Authorization</c>.
    /// </summary>
    /// <remarks>
    /// Requires naming the origin exactly - the specification forbids credentials alongside a
    /// <c>*</c> origin, and browsers enforce it - so this and <see cref="AllowAnyOrigin"/> are
    /// mutually exclusive and the filter will not emit both.
    /// </remarks>
    public bool AllowCredentials { get; set; }

    /// <summary>
    /// How long a browser may cache a preflight, in seconds.
    /// </summary>
    public int MaxAgeSec { get; set; } = 86400;

    /// <summary>
    /// The verbs advertised when the routing table cannot say - a preflight for a path no table
    /// recognises, or an application with no routing at all.
    /// </summary>
    /// <remarks>
    /// Only a fallback. When the table can answer, the real verb set for the path is used, because
    /// advertising <c>DELETE</c> on a read-only resource tells a client something untrue.
    /// </remarks>
    public string FallbackMethods { get; set; } = "GET, POST, PUT, DELETE, OPTIONS";

    public void AllowOrigin(string origin) {
        _allowedOrigins.Add(Normalize(origin));
    }

    /// <summary>
    /// Allow every subdomain of <paramref name="domain"/> - <c>AllowOriginSuffix("example.com")</c>
    /// admits <c>https://app.example.com</c>.
    /// </summary>
    /// <remarks>
    /// Matched against the host with a leading dot, so <c>example.com</c> does not also admit
    /// <c>notexample.com</c>. The apex itself is not included; add it with
    /// <see cref="AllowOrigin"/> if it is wanted.
    /// </remarks>
    public void AllowOriginSuffix(string domain) {
        _allowedOriginSuffixes.Add("." + domain.TrimStart('.').TrimEnd('/').ToLowerInvariant());
    }

    public void AllowHeader(string header) {
        _allowedHeaders.Add(header);
    }

    /// <summary>
    /// Let scripts read <paramref name="header"/> off the response.
    /// </summary>
    public void ExposeHeader(string header) {
        if (!_exposedHeaders.Contains(header, StringComparer.OrdinalIgnoreCase)) {
            _exposedHeaders.Add(header);
        }
    }

    /// <summary>
    /// Reads comma-separated origins from the configured environment variable and adds them to the
    /// allowed set.
    /// </summary>
    public void LoadFromEnvironment() {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        foreach (var origin in value.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (origin == "*") {
                AllowAnyOrigin = true;
            }
            else if (origin.StartsWith("*.", StringComparison.Ordinal)) {
                AllowOriginSuffix(origin.Substring(2));
            }
            else {
                AllowOrigin(origin);
            }
        }
    }

    /// <summary>Whether anything is configured at all.</summary>
    public bool IsConfigured =>
        AllowAnyOrigin || _allowedOrigins.Count > 0 || _allowedOriginSuffixes.Count > 0;

    public bool IsOriginAllowed(string origin) {
        if (AllowAnyOrigin) {
            return true;
        }

        var normalized = Normalize(origin);

        if (_allowedOrigins.Contains(normalized)) {
            return true;
        }

        if (_allowedOriginSuffixes.Count == 0) {
            return false;
        }

        // Compared against the host rather than the whole origin, so a suffix rule cannot be
        // satisfied by a path or a port that merely ends the right way.
        var host = Host(normalized);

        foreach (var suffix in _allowedOriginSuffixes) {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether every header the preflight asked about is allowed.</summary>
    public bool AreHeadersAllowed(IEnumerable<string> requested) {
        foreach (var header in requested) {
            if (!_allowedHeaders.Contains(header)) {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string origin) => origin.Trim().TrimEnd('/');

    private static string Host(string origin) {
        var scheme = origin.IndexOf("://", StringComparison.Ordinal);
        var start = scheme < 0 ? 0 : scheme + 3;
        var port = origin.IndexOf(':', start);

        return port < 0 ? origin.Substring(start) : origin.Substring(start, port - start);
    }
}
