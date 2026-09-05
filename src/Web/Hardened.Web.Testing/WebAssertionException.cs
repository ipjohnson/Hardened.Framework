namespace Hardened.Web.Testing;

/// <summary>
/// What <see cref="IWebAssertThat"/> throws for a status it was not asked for.
/// </summary>
/// <remarks>
/// The harness's own rather than a runner's assertion exception, because the harness names no
/// runner: xUnit and NUnit both report any exception a test throws as that test's failure, with
/// this message.
/// </remarks>
public sealed class WebAssertionException : Exception {

    public WebAssertionException(string message) : base(message) {
    }
}
