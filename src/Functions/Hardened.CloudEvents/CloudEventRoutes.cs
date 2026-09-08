namespace Hardened.CloudEvents;

/// <summary>
/// The routes an adapter maps a CloudEvent onto.
/// </summary>
/// <remarks>
/// <para>
/// Written once here rather than in each adapter, so an <c>[Event]</c> handler is reached by the
/// same route on Cloud Run as on Azure Functions: <c>EVENT /{source}/{type}</c>, the shape the
/// EventBridge adapter routes a bus event under. The other helper is what the Google adapters
/// route a Pub/Sub topic, a Storage bucket or a Firestore collection on - the last segment of the
/// resource name the event carries.
/// </para>
/// <para>
/// A source is a URI reference and may contain slashes, which the route keeps. Whether the
/// handler's attribute names the whole source or only its last segment is the adapter's decision
/// and is documented on the attribute's page for that cloud.
/// </para>
/// </remarks>
public static class CloudEventRoutes {
    /// <summary>The scheme an event routes under.</summary>
    public const string EventScheme = "EVENT";

    /// <summary><c>/{source}/{type}</c>.</summary>
    public static string Event(CloudEvent cloudEvent) => Event(cloudEvent.Source, cloudEvent.Type);

    /// <summary><c>/{source}/{type}</c>.</summary>
    public static string Event(string source, string type) => "/" + source + "/" + type;

    /// <summary>
    /// The last segment of a resource name: <c>orders</c> for
    /// <c>//pubsub.googleapis.com/projects/p/topics/orders</c>, and the whole value when it has no
    /// slash. Empty for an empty value, which is a route no handler declared rather than a failure
    /// inside an adapter.
    /// </summary>
    public static string LastSegment(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return "";
        }

        var trimmed = value!.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');

        return slash > -1 ? trimmed.Substring(slash + 1) : trimmed;
    }
}
