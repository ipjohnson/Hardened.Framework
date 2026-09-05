using System.Reflection;
using System.Runtime.ExceptionServices;
using Hardened.Requests.Abstract.Responses;
using Xunit;
using Xunit.v3;

namespace Hardened.Web.Testing;

/// <summary>
/// Asserting a call through a client answered what the contract says it answers.
/// </summary>
/// <remarks>
/// <para>
/// The response type is the assertion:
/// </para>
/// <code>
/// var created = await client.Todos.PostAsync(new NewTodo { Title = "ship it" })
///     .Returns&lt;Created&lt;Todo&gt;&gt;();
///
/// Assert.Equal($"/todos/{created.Value.Id}", created.Location);
///
/// var refused = await client.Todos[9999].GetAsync().Returns&lt;NotFound&lt;Problem&gt;&gt;();
///
/// Assert.Contains("9999", refused.Body.Detail);
/// </code>
/// <para>
/// Which is a status, a body type and a header set named once, in the vocabulary the handler and
/// the document already use, instead of an <c>Assert.ThrowsAsync</c> for the refusals, a separate
/// <c>Assert.Equal</c> for the status the exception happened to carry, and nothing at all for the
/// ones the client returns.
/// </para>
/// <para>
/// This package names no generator, so how the status, the body and the headers are reached is
/// the route's: an <see cref="ITestClientReader"/> the test assembly named, through the attribute a
/// client testing package ships. The readers are asked in the order the assembly named them, so a
/// solution with a Kiota client and a Refit interface declares both attributes and each call is
/// read by the route that recognises it. What is done with the answer is
/// <see cref="ResponseExpectation.Match{TExpected}"/>, so a wrong status reads the same whichever
/// client reported it.
/// </para>
/// </remarks>
public static class ClientAssertions {

    /// <summary>
    /// The response type the call was expected to answer with, built from what it answered.
    /// </summary>
    /// <typeparam name="TExpected">
    /// A Hardened response type - <c>Created&lt;Todo&gt;</c>, <c>NotFound&lt;Problem&gt;</c>,
    /// <c>NoContent</c>. Its status is what the answer is checked against, and its type argument
    /// is the body type: what the client is held to having deserialised, or what a body it handed
    /// over as text is read as.
    /// </typeparam>
    /// <exception cref="InvalidOperationException">
    /// Another status was answered, the body was not the declared type, a header the status
    /// carries was absent, or no route the assembly named could read the call.
    /// </exception>
    public static async Task<TExpected> Returns<TExpected>(this Task call)
        where TExpected : IResponseExpectation<TExpected> {

        ArgumentNullException.ThrowIfNull(call);

        var expectation = "Returns<" + ResponseExpectation.Name(typeof(TExpected)) + ">()";
        var answer = await Answer(call, expectation, BodyTypeOf(typeof(TExpected)));

        try {
            return ResponseExpectation.Match<TExpected>(answer.Status, answer.Body, answer.Headers);
        } catch (InvalidOperationException failure) when (answer.Caveat != null) {
            throw new InvalidOperationException(failure.Message + " " + answer.Caveat, failure);
        }
    }

    /// <summary>
    /// The status the call was expected to answer with, and nothing about its body.
    /// </summary>
    /// <typeparam name="TStatus">
    /// A Hardened response type, including the ones that are not expectations because they state
    /// something the wire does not carry back - <c>NotFound</c>, <c>Conflict</c>.
    /// </typeparam>
    /// <exception cref="InvalidOperationException">
    /// Another status was answered, or no route the assembly named could read the call.
    /// </exception>
    public static async Task ReturnsStatus<TStatus>(this Task call) where TStatus : IDeclaresStatus {
        ArgumentNullException.ThrowIfNull(call);

        var expectation = "ReturnsStatus<" + ResponseExpectation.Name(typeof(TStatus)) + ">()";
        var answer = await Answer(call, expectation, bodyType: null);

        ResponseExpectation.MatchStatus<TStatus>(answer.Status, answer.Body);
    }

    /// <summary>
    /// What the call was answered with, read by the first route that recognises it.
    /// </summary>
    /// <remarks>
    /// A failure no route recognises is not a refusal - a timeout, a serializer that could not read
    /// a success - and reaches the test as it was thrown rather than wrapped as an answer.
    /// </remarks>
    private static async Task<ClientAnswer> Answer(Task call, string expectation, Type? bodyType) {
        var readers = Readers(expectation);

        Exception? thrown = null;

        try {
            await call;
        } catch (Exception failure) {
            thrown = failure;
        }

        var result = thrown == null ? TaskResult.Of(call) : null;

        foreach (var reader in readers) {
            if (await reader.Read(result, thrown, bodyType) is { } answer) {
                return answer;
            }
        }

        if (thrown != null) {
            ExceptionDispatchInfo.Capture(thrown).Throw();
        }

        throw new InvalidOperationException(
            $"{expectation} has no route that read this call, which returned " +
            (result == null ? "no value" : "a " + ResponseExpectation.Name(result.GetType())) + ". " +
            string.Join(" ", readers.Select(reader => reader.Unreadable)));
    }

    /// <summary>The readers the running test's assembly named, in the order it named them.</summary>
    private static IReadOnlyList<ITestClientReader> Readers(string expectation) {
        if (TestContext.Current.TestClass is not IXunitTestClass { Class: var testClass }) {
            throw new InvalidOperationException(
                $"{expectation} reads the call through the routes the test assembly named, which " +
                "needs a running xUnit test, and there is none.");
        }

        return ReadersOf(testClass.Assembly, expectation);
    }

    internal static IReadOnlyList<ITestClientReader> ReadersOf(Assembly testAssembly, string expectation) {
        var readers = TestClientBuilder.ReadersFor(testAssembly);

        if (readers.Count == 0) {
            throw new InvalidOperationException(
                $"{expectation} reads the call through a route that can read answers, and " +
                $"{testAssembly.GetName().Name} names none. Declare the client testing package's " +
                "assembly attribute - the one that builds the client for a test parameter.");
        }

        return readers;
    }

    /// <summary>
    /// The type a body handed over as text is read as: the expectation's type argument that is not
    /// a status marker - the <c>Problem</c> of <c>NotFound&lt;Problem&gt;</c>, the <c>TBody</c> of
    /// <c>Status&lt;TCode, TBody&gt;</c> - or null for an expectation that declares none.
    /// </summary>
    /// <remarks>
    /// A convention, and the one the shipped response types all follow. An application's own
    /// expectation type that carries a body declares one type argument for it.
    /// </remarks>
    internal static Type? BodyTypeOf(Type expected) {
        if (!expected.IsGenericType) {
            return null;
        }

        var bodies = expected.GetGenericArguments()
            .Where(argument => !typeof(IStatusCode).IsAssignableFrom(argument))
            .ToArray();

        return bodies.Length switch {
            0 => null,
            1 => bodies[0],
            _ => throw new InvalidOperationException(
                $"{ResponseExpectation.Name(expected)} has {bodies.Length} type arguments, so " +
                "Returns cannot tell which one a body is read as. An expectation that carries a " +
                "body declares one type argument for it."),
        };
    }
}
