using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Hardened1.Tests;

/// <summary>
/// The published document's status set, operation by operation, against the statuses this
/// application answers when it is asked.
/// </summary>
/// <remarks>
/// A declared status nothing answers - or an answered status the document never mentions - is the
/// defect class a reference page cannot reveal, because the page renders either way. So the table
/// below holds requests rather than status codes. Every probe is sent, what came back is what
/// counts as answered, and the document is compared against that. A status cannot be claimed here
/// without a request that produces it, and a probe that stops producing the status it was written
/// for is reported beside the comparison rather than quietly changing what this test asserts.
///
/// Each request runs against a container of its own, so the probes need no ordering and one cannot
/// set up or spoil another. See "Testing" in README.md.
/// </remarks>
public class DocumentStatusTests {

    /// <summary>
    /// One request, and the status it exists to provoke.
    /// </summary>
    /// <param name="Operation">
    /// The document's own key for the route: the method, then the templated path.
    /// </param>
    private sealed record Probe(
        string Operation,
        int Status,
        Func<ITestWebApp, Task<TestWebResponse>> Send);

    private record NewTodoRequest(string Title);

    /// <summary>
    /// A request per status this application can answer.
    /// </summary>
    /// <remarks>
    /// The success statuses differ by response model, and that difference is the model's point
    /// rather than an inconsistency: throws mode names one success type per handler and has
    /// nowhere to put a status beside it, so a created todo comes back at 200. Every other model,
    /// and both contract languages, carry the status in the declaration.
    /// </remarks>
    private static readonly Probe[] Probes = [
        new("GET /todos", 200, app => app.Get("/todos")),

        new("GET /todos/{id}", 200, app => app.Get("/todos/1")),
        new("GET /todos/{id}", 400, app => app.Get("/todos/0")),
        new("GET /todos/{id}", 404, app => app.Get("/todos/9999")),

#if (codeFirst && throwsMode)
        new("POST /todos", 200, app => app.Post(new NewTodoRequest("Write a test"), "/todos")),
#else
        new("POST /todos", 201, app => app.Post(new NewTodoRequest("Write a test"), "/todos")),
#endif
        new("POST /todos", 400, app => app.Post(new NewTodoRequest(new string('x', 100)), "/todos")),
        new("POST /todos", 409, app => app.Post(new NewTodoRequest("Add an endpoint"), "/todos")),

#if (codeFirst && throwsMode)
        new("DELETE /todos/{id}", 200, app => app.Delete("/todos/1")),
#else
        new("DELETE /todos/{id}", 204, app => app.Delete("/todos/1")),
#endif
        new("DELETE /todos/{id}", 400, app => app.Delete("/todos/0")),
        new("DELETE /todos/{id}", 404, app => app.Delete("/todos/9999"))
    ];

    [HardenedTest]
    public async Task TheDocumentDeclaresExactlyWhatTheApplicationAnswers(ITestWebApp app) {
        var faults = new List<string>();
        var answered = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var probe in Probes) {
            var status = (await probe.Send(app)).StatusCode;

            if (status != probe.Status) {
                faults.Add(
                    $"{probe.Operation}: the probe written for {probe.Status} answered {status}.");
            }

            if (!answered.TryGetValue(probe.Operation, out var statuses)) {
                answered[probe.Operation] = statuses = new SortedSet<string>(StringComparer.Ordinal);
            }

            // What came back, not what the probe hoped for. A wrong probe is reported above and
            // still contributes the truth here, so one mistake reads as one fault rather than two.
            statuses.Add(status.ToString(CultureInfo.InvariantCulture));
        }

        var declared = Declared(await Document(app));

        foreach (var operation in declared.Keys.Union(answered.Keys).Order(StringComparer.Ordinal)) {
            var inDocument = declared.TryGetValue(operation, out var published)
                ? published
                : new SortedSet<string>(StringComparer.Ordinal);

            var onTheWire = answered.TryGetValue(operation, out var observed)
                ? observed
                : new SortedSet<string>(StringComparer.Ordinal);

            if (inDocument.Count == 0) {
                faults.Add($"{operation}: answered here and absent from the document.");
                continue;
            }

            if (onTheWire.Count == 0) {
                faults.Add($"{operation}: in the document and reached by no probe in this file.");
                continue;
            }

            foreach (var status in inDocument.Except(onTheWire)) {
                faults.Add($"{operation}: declares {status} and no request here answered it.");
            }

            foreach (var status in onTheWire.Except(inDocument)) {
                faults.Add($"{operation}: answered {status}, which the document does not declare.");
            }
        }

        var report = string.Join(Environment.NewLine, faults);

#if (xunit)
        Assert.True(faults.Count == 0, report);
#else
        Assert.That(faults, Is.Empty, report);
#endif
    }

    /// <summary>
    /// The status set the served document declares, keyed the way <see cref="Probe"/> keys one.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> Declared(JsonElement document) {
        var declared = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var path in document.GetProperty("paths").EnumerateObject()) {
            foreach (var operation in path.Value.EnumerateObject()) {
                if (!operation.Value.TryGetProperty("responses", out var responses)) {
                    continue;
                }

                declared[$"{operation.Name.ToUpperInvariant()} {path.Name}"] = new SortedSet<string>(
                    responses.EnumerateObject().Select(response => response.Name),
                    StringComparer.Ordinal);
            }
        }

        return declared;
    }

    /// <summary>The served document, which is stored and answered gzipped.</summary>
    private static async Task<JsonElement> Document(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        await using var gzip = new GZipStream(response.Body, CompressionMode.Decompress);

        return JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;
    }
}
