using System.Reflection;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The generic problem types, for a caller who already has a payload type.
/// </summary>
/// <remarks>
/// <para>
/// The hand-written equivalent of what the specification-first emitter generates for a declared
/// status - <c>GetPetNotFound(ApiError Body)</c> - so both front ends describe a declared error the
/// same way. It also makes two statuses sharing one payload expressible:
/// <c>Response&lt;Todo, NotFound&lt;ApiError&gt;, Conflict&lt;ApiError&gt;&gt;</c> is two distinct
/// closed types, where <c>Response&lt;Todo, ApiError, ApiError&gt;</c> is CS0457.
/// </para>
/// <para>
/// The property that has to hold is that the payload reaches the wire rather than a wrapper around
/// it. That is <see cref="ICarriesResponseBody"/>, and the dispatch reads it at compile time.
/// </para>
/// </remarks>
public class GenericResponseTests {

    public sealed record ApiError(string Code, string Message);

    /// <summary>
    /// Every generic problem type, found by reflection rather than listed, so one added later is
    /// covered without anyone remembering.
    /// </summary>
    public static TheoryData<Type> GenericProblemTypes {
        get {
            var data = new TheoryData<Type>();

            foreach (var type in typeof(NotFound<>).Assembly.GetExportedTypes()) {
                if (type.IsGenericTypeDefinition &&
                    type.GetCustomAttribute<HttpStatusAttribute>() != null &&
                    typeof(ICarriesResponseBody).IsAssignableFrom(type)) {
                    data.Add(type);
                }
            }

            return data;
        }
    }

    private static object Build(Type type) {
        var closed = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(ApiError)) : type;
        var constructor = closed.GetConstructors().OrderBy(c => c.GetParameters().Length).First();

        var arguments = constructor.GetParameters()
            .Select(p =>
                p.ParameterType == typeof(ApiError) ? new ApiError("e", "m") :
                p.ParameterType == typeof(TimeSpan) ? TimeSpan.FromSeconds(5) :
                p.ParameterType == typeof(string) ? "/things/1" :
                p.HasDefaultValue ? p.DefaultValue :
                Activator.CreateInstance(p.ParameterType))
            .ToArray();

        return constructor.Invoke(arguments);
    }

    #region the body reaches the wire

    /// <summary>
    /// The whole point. Sending the wrapper would nest the caller's payload under a Body member and
    /// ship Type, Title and Status beside it - a shape they did not ask for and no client expects.
    /// </summary>
    [Theory]
    [MemberData(nameof(GenericProblemTypes))]
    public void TheBodyIsThePayloadNotTheWrapper(Type openGeneric) {
        var response = (ICarriesResponseBody)Build(openGeneric);

        var body = Assert.IsType<ApiError>(response.Body);

        Assert.Equal("e", body.Code);
    }

    /// <summary>
    /// Declared explicitly, so it is reachable through the interface and not through the record's
    /// own <c>Body</c> - which is what the generated cast in the dispatch goes through.
    /// </summary>
    [Theory]
    [MemberData(nameof(GenericProblemTypes))]
    public void EveryGenericProblemCarriesItsBody(Type openGeneric) {
        Assert.True(
            typeof(ICarriesResponseBody).IsAssignableFrom(openGeneric),
            openGeneric.Name + " must implement ICarriesResponseBody.");
    }

    /// <summary>
    /// Created&lt;T&gt; had this noted as outstanding when it was written; it is the same case.
    /// </summary>
    [Fact]
    public void CreatedCarriesItsValueRatherThanItself() {
        ICarriesResponseBody created = new Created<ApiError>(new ApiError("e", "m"), "/things/1");

        Assert.IsType<ApiError>(created.Body);
    }

    #endregion

    #region same problem kind as the non-generic form

    /// <summary>
    /// The <c>type</c> URI identifies what went wrong, not what shape the body is - so the generic
    /// and non-generic forms of one status agree on it, and on the title.
    /// </summary>
    /// <remarks>
    /// Every pair rather than a sample. The two are written out separately, so a copied-and-edited
    /// declaration that kept the wrong <c>ProblemTypes</c> constant would read correctly and
    /// describe the wrong problem - which a client matching on <c>type</c> acts on.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GenericProblemTypes))]
    public void AGenericProblemAgreesWithItsNonGenericFormOnKind(Type openGeneric) {
        var plain = openGeneric.Assembly.GetType(
            openGeneric.Namespace + "." + openGeneric.Name.Split('`')[0]);

        // Created<T> has no non-generic counterpart and is not a problem type.
        if (plain == null) {
            return;
        }

        var generic = Build(openGeneric);
        var reference = Build(plain);

        foreach (var member in new[] { "Type", "Title" }) {
            Assert.Equal(
                plain.GetProperty(member)!.GetValue(reference),
                openGeneric.MakeGenericType(typeof(ApiError)).GetProperty(member)!.GetValue(generic));
        }
    }

    [Theory]
    [MemberData(nameof(GenericProblemTypes))]
    public void TheAttributeAgreesWithTheStatusProperty(Type openGeneric) {
        var declared = openGeneric.GetCustomAttribute<HttpStatusAttribute>()!.StatusCode;

        Assert.Equal(declared, ((IHttpStatusResponse)Build(openGeneric)).Status);
    }

    [Theory]
    [MemberData(nameof(GenericProblemTypes))]
    public void EveryGenericProblemIsSealed(Type openGeneric) {
        Assert.True(openGeneric.IsSealed, openGeneric.Name + " must be sealed.");
    }

    #endregion

    #region headers still come from the response

    [Fact]
    public void TheGenericRateLimitedStillWritesRetryAfter() {
        var headers = new Dictionary<string, StringValues>();

        new RateLimited<ApiError>(TimeSpan.FromSeconds(30), new ApiError("e","m"))
            .ApplyHeaders(headers);

        Assert.Equal("30", headers["Retry-After"]);
    }

    [Fact]
    public void TheGenericUnauthorizedStillChallenges() {
        var headers = new Dictionary<string, StringValues>();

        new Unauthorized<ApiError>(new ApiError("e","m")).ApplyHeaders(headers);

        Assert.Equal("Bearer", headers[AuthorizationChallenge.HeaderName]);
    }

    [Fact]
    public void TheGenericServiceUnavailableWritesNoRetryAfterWithoutOne() {
        var headers = new Dictionary<string, StringValues>();

        new ServiceUnavailable<ApiError>(new ApiError("e","m")).ApplyHeaders(headers);

        Assert.False(headers.ContainsKey("Retry-After"));
    }

    #endregion

    #region composing a response set

    /// <summary>
    /// Two statuses sharing one payload type, which is the shape this exists for.
    /// <c>Response&lt;Todo, ApiError, ApiError&gt;</c> is CS0457; these are distinct closed types.
    /// </summary>
    [Fact]
    public void TwoStatusesCanShareOnePayloadType() {
        Response<string, NotFound<ApiError>, Conflict<ApiError>> notFound =
            new NotFound<ApiError>(new ApiError("nf", "no such todo"));

        Response<string, NotFound<ApiError>, Conflict<ApiError>> conflict =
            new Conflict<ApiError>(new ApiError("cf", "already exists"));

        Assert.IsType<NotFound<ApiError>>(notFound.Value);
        Assert.IsType<Conflict<ApiError>>(conflict.Value);
    }

    /// <summary>
    /// And it can be thrown, keeping its type - the generic exception composes with the generic
    /// response because both are ordinary types.
    /// </summary>
    [Fact]
    public void AGenericProblemCanBeThrownWithItsTypeIntact() {
        var response = new NotFound<ApiError>(new ApiError("nf", "no such todo"));

        try {
            throw response.AsException();
        }
        catch (ResponseException<NotFound<ApiError>> e) {
            Assert.Equal(404, e.StatusCode);
            Assert.Equal("nf", e.Response.Body.Code);
        }
    }

    #endregion
}
