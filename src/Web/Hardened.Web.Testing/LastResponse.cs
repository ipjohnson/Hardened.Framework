using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Web.Testing;

/// <summary>
/// The most recent response the pipeline answered inside the current test, whether it went out
/// through an <see cref="HttpClient"/> or through <see cref="ITestWebApp"/>.
/// </summary>
/// <remarks>
/// <para>
/// A refusal is asserted with <c>Assert.ThrowsAsync</c> against whatever the client library throws.
/// What no client library surfaces is the response it did not throw on: the 201 and its
/// <c>Location</c>, a 204, an <c>ETag</c>. The transport sees all of them, so it keeps the last one
/// here, with no application in the signature:
/// </para>
/// <code>
/// var todo = await client.Todos.PostAsync(new NewTodo { Title = "ship it" });
///
/// Assert.Equal(201, LastResponse.Status);
/// Assert.Equal($"/todos/{todo!.Id}", LastResponse.Headers["Location"]);
/// </code>
/// <para>
/// Keyed on xUnit v3's <see cref="TestContext.Current"/>, which is per test and flows through
/// async code, rather than on an <c>AsyncLocal</c> of the harness's own. The
/// <c>DependencyModules</c> runner invokes the test method inside xUnit's own test pipeline, so the
/// context names the test everywhere an assertion can run; <c>LastResponseTests</c> is what says so
/// rather than this remark. It builds the container and resolves the parameters earlier, inside
/// the test-method stage, where xUnit has the method in scope and no test or test case yet - a
/// request answered there is not kept, and reading this there says no test is running.
/// </para>
/// <para>
/// Reading it before anything was answered fails naming the test, because a stale response from
/// another test is the one answer this must never give.
/// </para>
/// </remarks>
public static class LastResponse {

    private static readonly ConditionalWeakTable<ITest, Recorded> Responses = new();

    /// <summary>The status the pipeline answered, 200 where it set none.</summary>
    public static int Status => Current().Status;

    /// <summary>Every response header, matched without regard to case.</summary>
    public static IReadOnlyDictionary<string, StringValues> Headers => Current().Headers;

    public static string? ContentType => Current().ContentType;

    /// <summary>The body as the pipeline wrote it, content coding included.</summary>
    public static byte[] Body => Current().Body;

    /// <summary>Whether the current test has had a response answered.</summary>
    public static bool IsAvailable => Key() is { } key && Responses.TryGetValue(key, out _);

    internal static void Record(IExecutionResponse response, byte[] body) {
        if (Key() is not { } key) {
            return;
        }

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers) {
            headers[header.Key] = header.Value;
        }

        var recorded = new Recorded(response.Status ?? 200, headers, response.ContentType, body);

        Responses.AddOrUpdate(key, recorded);
    }

    private static Recorded Current() {
        if (Key() is not { } key) {
            throw new InvalidOperationException(
                "LastResponse is kept per running xUnit test, and there is no test running.");
        }

        if (!Responses.TryGetValue(key, out var recorded)) {
            throw new InvalidOperationException(
                $"LastResponse has nothing to report: no request has been answered through the pipeline in '{key.TestDisplayName}'. " +
                "Send one through ITestWebApp, or through a client it built, before reading it.");
        }

        return recorded;
    }

    /// <summary>
    /// The running test, which xUnit holds for exactly as long as the test lives - what lets the
    /// table be weak - or null outside one.
    /// </summary>
    private static ITest? Key() => TestContext.Current.Test;

    private sealed record Recorded(
        int Status,
        IReadOnlyDictionary<string, StringValues> Headers,
        string? ContentType,
        byte[] Body);
}
