namespace Hardened.Web.Runtime.Cors;

public class CorsConfiguration {
    public const string DefaultEnvironmentVariable = "CORS_ALLOWED_ORIGINS";

    private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);

    public string EnvironmentVariable { get; set; } = DefaultEnvironmentVariable;

    public IReadOnlySet<string> AllowedOrigins => _allowedOrigins;

    public string AllowedMethods { get; set; } = "GET, POST, PUT, DELETE, OPTIONS";

    public string AllowedHeaders { get; set; } = "Authorization, Content-Type, Accept, x-auth-token, x-amz-content-sha256";

    public int MaxAgeSec { get; set; } = 86400;

    public void AllowOrigin(string origin) {
        _allowedOrigins.Add(origin.TrimEnd('/'));
    }

    /// <summary>
    /// Reads comma-separated origins from the configured environment variable
    /// and adds them to the allowed set.
    /// </summary>
    public void LoadFromEnvironment() {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value)) return;

        foreach (var origin in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            AllowOrigin(origin);
        }
    }

    public bool IsOriginAllowed(string origin) {
        return _allowedOrigins.Contains(origin.TrimEnd('/'));
    }
}
