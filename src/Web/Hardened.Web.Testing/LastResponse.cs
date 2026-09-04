using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;
using Xunit;

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
/// <c>DependencyModules</c> runner builds the container and runs the startup attributes inside
/// xUnit's own test-case pipeline and invokes the method inside its test pipeline, so the context
/// is in place for both; <c>LastResponseTests</c> is what says so rather than this remark. A
/// request answered while the test case is being prepared is kept under the case, and one answered
/// in the test body under the test, which is where the assertions are.
/// </para>
/// <para>
/// Reading it before anything was answered fails naming the test, because a stale response from
/// another test is the one answer this must never give.
/// </para>
/// </remarks>
public static class LastResponse {

    private static readonly ConditionalWeakTable<object, Recorded> Responses = new();

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
        var context = TestContext.Current;

        if (Key() is not { } key) {
            throw new InvalidOperationException(
                "LastResponse is kept per running xUnit test, and there is no test running.");
        }

        if (!Responses.TryGetValue(key, out var recorded)) {
            var name = context.Test?.TestDisplayName ?? context.TestCase?.TestCaseDisplayName ?? "this test";

            throw new InvalidOperationException(
                $"LastResponse has nothing to report: no request has been answered through the pipeline in '{name}'. " +
                "Send one through ITestWebApp, or through a client it built, before reading it.");
        }

        return recorded;
    }

    /// <summary>
    /// The test if one is running, else the test case being prepared - both per test and both
    /// held by xUnit for exactly as long as the test lives, which is what lets the table be weak.
    /// </summary>
    private static object? Key() {
        var context = TestContext.Current;

        return (object?)context.Test ?? context.TestCase;
    }

    private sealed record Recorded(
        int Status,
        IReadOnlyDictionary<string, StringValues> Headers,
        string? ContentType,
        byte[] Body);
}
