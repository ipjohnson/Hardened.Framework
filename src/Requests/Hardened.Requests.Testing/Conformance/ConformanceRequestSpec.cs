namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// A request described in transport-neutral terms.
///
/// Each transport adapter translates one of these into its own native representation
/// (an <c>HttpContext</c>, an <c>APIGatewayHttpApiV2ProxyRequest</c>, an SQS message, and
/// so on) and then into an <see cref="Hardened.Requests.Abstract.Execution.IExecutionRequest"/>.
/// The conformance suite then asserts that what comes out the far side is the same
/// regardless of which transport carried it.
/// </summary>
public class ConformanceRequestSpec {
    public string Method { get; set; } = "GET";

    public string Path { get; set; } = "/";

    /// <summary>
    /// Header names are matched case-insensitively, as they are over the wire.
    /// </summary>
    public IDictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, string> QueryString { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Raw cookie strings in <c>name=value</c> form, matching the shape API Gateway v2
    /// delivers them in.
    /// </summary>
    public IList<string> Cookies { get; } = new List<string>();

    public byte[]? Body { get; set; }
}
