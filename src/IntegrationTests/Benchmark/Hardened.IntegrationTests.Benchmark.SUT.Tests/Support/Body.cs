namespace Hardened.IntegrationTests.Benchmark.SUT.Tests.Support;

/// <summary>
/// Reads a response body as the bytes that went to the wire.
/// </summary>
/// <remarks>
/// Deliberately not <c>Deserialize&lt;T&gt;</c>. Half of what this suite checks is whether a body
/// was serialized at all - a plain-text route that came back JSON-encoded still deserializes into a
/// string, and reading it that way would agree with the bug.
/// </remarks>
public static class Body {
    public static async Task<string> Read(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }
}
