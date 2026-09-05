namespace Hardened.Web.Testing;

/// <summary>
/// A route that can also read what a call through a client it built was answered with.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClientAssertions.Returns{TExpected}"/> awaits the call and asks each reader the
/// assembly named, in the order it named them, until one recognises the answer. A reader
/// recognises its own generator's shapes and nothing else - the exception type its client throws,
/// the envelope its client returns, the response its own handler recorded - and answers null for
/// anything else, which is what lets two routes coexist in one assembly.
/// </para>
/// <para>
/// Where the client hands an error body over undeserialised, as Refit does, it is read as the type
/// the expectation declares; that is what the body type is for, and a client that deserialised it
/// already ignores it.
/// </para>
/// </remarks>
public interface ITestClientReader {

    /// <summary>The answer, or null when the call is not one this reader recognises.</summary>
    /// <param name="result">The value the call completed with, or null where it completed with none or threw.</param>
    /// <param name="thrown">What the call threw, or null where it completed.</param>
    /// <param name="bodyType">
    /// The type the expectation declares for the body, or null where it declares none or only the
    /// status is asked for.
    /// </param>
    Task<ClientAnswer?> Read(object? result, Exception? thrown, Type? bodyType);

    /// <summary>What makes a call unreadable here, for the failure that says no route read it.</summary>
    string Unreadable { get; }
}
