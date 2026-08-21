using System.Globalization;
using System.Linq;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The branches a response set takes when it holds nothing, and the ones ResponseException takes
/// for a response that carries neither a body nor headers.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResponseArityTests</c> invokes every constructor and every conversion, so the populated half
/// of <c>ToString</c> runs at all seven arities. The empty half never did: nothing constructs a
/// <c>default</c> response set, because nothing is supposed to - and a branch nobody takes is still
/// a branch the type declares seven times over.
/// </para>
/// <para>
/// Worth asserting rather than only covering. <c>default(Response&lt;…&gt;)</c> is reachable from
/// ordinary code - an uninitialised field, an array element, a <c>default</c> in a switch arm - and
/// what it renders is what lands in a log line when a handler returns one by accident.
/// </para>
/// </remarks>
public class DefaultResponseTests {

    private static Type Closed(int arity) =>
        typeof(Response<,>).Assembly
            .GetExportedTypes()
            .Single(t => t.IsGenericTypeDefinition &&
                         t.Name == "Response`" + arity.ToString(CultureInfo.InvariantCulture))
            .MakeGenericType(
                new[] {
                    typeof(string), typeof(int), typeof(bool), typeof(Guid),
                    typeof(TimeSpan), typeof(Uri), typeof(NotFound), typeof(Conflict)
                }.Take(arity).ToArray());

    public static TheoryData<int> Arities {
        get {
            var data = new TheoryData<int>();

            for (var arity = 2; arity <= 8; arity++) {
                data.Add(arity);
            }

            return data;
        }
    }

    /// <summary>
    /// A response set holding nothing renders as the empty string rather than throwing or reading
    /// "null" - at every arity, because the expression is written out once per arity.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void ADefaultResponseRendersEmptyAtEveryArity(int arity) {
        var closed = Closed(arity);

        var empty = Activator.CreateInstance(closed);

        Assert.Equal(string.Empty, empty!.ToString());
    }

    /// <summary>And it holds no case, which is what makes the render above the right answer.</summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void ADefaultResponseHoldsNoCase(int arity) {
        var closed = Closed(arity);

        var empty = Activator.CreateInstance(closed);

        Assert.Null(closed.GetProperty("Value")!.GetValue(empty));
    }

    /// <summary>
    /// A bodyless response carries no value into the exception.
    /// </summary>
    /// <remarks>
    /// <c>NoContent</c> is the case: <c>HasBody</c> is false, so there is nothing to serialise and
    /// the exception must not offer one. The populated side of that conditional is covered by every
    /// other response type; this is the side that decides whether a 204 answers with the four
    /// characters "null".
    /// </remarks>
    [Fact]
    public void ABodylessResponseGivesTheExceptionNoValue() {
        var exception = new NoContent().AsException();

        Assert.Equal(204, exception.StatusCode);
        Assert.Null(exception.Value);
    }

    /// <summary>
    /// A response that provides no headers leaves the collection untouched.
    /// </summary>
    /// <remarks>
    /// <c>ApplyHeaders</c> type-tests for <c>IProvidesResponseHeaders</c>. The positive side is
    /// covered by Unauthorized and the two Retry-After types; the negative side is every other
    /// response, and it has to be a no-op rather than a throw.
    /// </remarks>
    [Fact]
    public void AResponseProvidingNoHeadersAppliesNone() {
        var headers = new Dictionary<string, StringValues>();

        new Conflict("clash").AsException().ApplyHeaders(headers);

        Assert.Empty(headers);
    }

    /// <summary>
    /// The default message names the status, and an explicit one replaces it.
    /// </summary>
    /// <remarks>
    /// Both sides of the same null-coalesce. The default is what a log shows for a response thrown
    /// without explanation, which is the common case and the one nobody writes a test for.
    /// </remarks>
    [Fact]
    public void TheDefaultMessageNamesTheStatus() {
        Assert.Contains("404", new NotFound("todo").AsException().Message);
    }

    [Fact]
    public void AnExplicitMessageReplacesTheDefault() {
        var exception = new NotFound("todo").AsException("nothing by that id");

        Assert.Equal("nothing by that id", exception.Message);
    }
}
