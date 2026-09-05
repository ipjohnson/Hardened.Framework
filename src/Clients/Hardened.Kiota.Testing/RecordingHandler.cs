using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Kiota.Testing;

/// <summary>
/// What the client received, for the responses it does not surface.
/// </summary>
/// <remarks>
/// <para>
/// Kiota reports a refusal by throwing a model that carries the status and the response headers, so
/// nothing here is needed for one. It reports a success by returning the body and nothing else: the
/// status is gone, and so is the <c>Location</c> on a 201, the <c>ETag</c> on a 304 and the fact
/// that a 204 was a 204 rather than a 200 with an empty body.
/// </para>
/// <para>
/// So the route puts this in the client's own chain. It is the response as the client's HTTP stack
/// saw it, one hop before the generated code reads it - not what the pipeline answered, which is
/// what <c>LastResponse</c> reports and is a different claim.
/// </para>
/// <para>
/// <b>The last response the client received in the current test.</b> Keyed on xUnit's
/// <see cref="TestContext.Current"/> the way <c>LastResponse</c> is, so two tests running in
/// parallel never see each other's. Within one test it is the most recent call, which is what
/// <c>await client.Todos.PostAsync(…).Returns&lt;Created&lt;Todo&gt;&gt;()</c> is asking about -
/// awaiting several calls at once and then asserting on one of them is not a shape this can
/// answer.
/// </para>
/// </remarks>
internal sealed class RecordingHandler : DelegatingHandler {

    private static readonly ConditionalWeakTable<ITest, Received> Responses = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {

        var response = await base.SendAsync(request, cancellationToken);

        if (TestContext.Current.Test is { } test) {
            Responses.AddOrUpdate(test, Read(response));
        }

        return response;
    }

    /// <summary>
    /// The status the client received in the current test and the headers that came with it, or
    /// false where no call through a recording client has been answered in it, or no test is running.
    /// </summary>
    public static bool TryCurrent([NotNullWhen(true)] out Received? received) {
        received = null;

        return TestContext.Current.Test is { } test && Responses.TryGetValue(test, out received);
    }

    /// <summary>
    /// Response headers and content headers together, because both are headers on the response and
    /// only the transport draws the line between them.
    /// </summary>
    private static Received Read(HttpResponseMessage response) {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers) {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers) {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new Received((int)response.StatusCode, headers);
    }

    internal sealed record Received(int Status, IReadOnlyDictionary<string, string> Headers);
}
