using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.Runtime.Execution;

/// <summary>
/// A delivery's transport, plus what Cloud Run says about the service it is running.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run sets <c>K_SERVICE</c>, <c>K_REVISION</c> and <c>K_CONFIGURATION</c> in every
/// service container. They are published under the OpenTelemetry names where one exists -
/// <c>faas.name</c> and <c>faas.version</c> are what the FaaS conventions call a serverless
/// function's name and version - and under a <c>gcp.</c> key for the configuration, which the
/// conventions do not name; that is the same namespace OpenTelemetry uses for the Cloud Run job
/// attributes. Everything else is answered by the connection the envelope arrived on.
/// </para>
/// <para>
/// Read from the environment per instance rather than cached for the process, so a test can set
/// a variable and see it; three lookups per trigger request are nothing beside decoding its body.
/// </para>
/// </remarks>
public sealed class CloudRunTransportInfo : ITransportInfo {
    /// <summary>The service's name, from <c>K_SERVICE</c>.</summary>
    public const string ServiceKey = "faas.name";

    /// <summary>The revision serving the request, from <c>K_REVISION</c>.</summary>
    public const string RevisionKey = "faas.version";

    /// <summary>The configuration that created the revision, from <c>K_CONFIGURATION</c>.</summary>
    public const string ConfigurationKey = "gcp.cloud_run.configuration";

    public const string ServiceVariable = "K_SERVICE";
    public const string RevisionVariable = "K_REVISION";
    public const string ConfigurationVariable = "K_CONFIGURATION";

    private static readonly string[] OwnKeys = [ServiceKey, RevisionKey, ConfigurationKey];

    private readonly ITransportInfo _inner;
    private readonly string? _service;
    private readonly string? _revision;
    private readonly string? _configuration;
    private IReadOnlyList<string>? _keys;

    /// <summary>Over <paramref name="inner"/>, with the three facts read from the environment.</summary>
    public CloudRunTransportInfo(ITransportInfo inner)
        : this(
            inner,
            Environment.GetEnvironmentVariable(ServiceVariable),
            Environment.GetEnvironmentVariable(RevisionVariable),
            Environment.GetEnvironmentVariable(ConfigurationVariable)) {
    }

    /// <summary>Over <paramref name="inner"/>, with the three facts stated outright.</summary>
    public CloudRunTransportInfo(ITransportInfo inner, string? service, string? revision, string? configuration) {
        _inner = inner;
        _service = Value(service);
        _revision = Value(revision);
        _configuration = Value(configuration);
    }

    /// <summary>The connection's keys, then the three of this transport's own.</summary>
    public IReadOnlyList<string> Keys => _keys ??= _inner.Keys.Concat(OwnKeys).Distinct().ToArray();

    public string? Get(string key) =>
        key switch {
            ServiceKey => _service,
            RevisionKey => _revision,
            ConfigurationKey => _configuration,
            _ => _inner.Get(key)
        };

    /// <summary>Null for an unset or empty variable, which is what "not on Cloud Run" looks like.</summary>
    private static string? Value(string? variable) => string.IsNullOrEmpty(variable) ? null : variable;
}
