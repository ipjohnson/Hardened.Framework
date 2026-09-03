using System.Reflection;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The built-in response types, and the two facts every one of them has to get right.
///
/// <para>
/// A type's status is a wire contract twice over: it is what the pipeline writes, and it is what
/// the generator reads to describe the endpoint in a document. Those come from two different places
/// - <c>[HttpStatus]</c> and <c>IHttpStatusResponse.Status</c> - which is deliberate, because one is
/// readable at compile time and the other without reflection at run time. The hazard that creates
/// is that they can disagree, and a document promising a 404 for a type that writes a 409 is worse
/// than either alone. The first test here is the one that makes that impossible to ship.
/// </para>
/// </summary>
public class BuiltInResponseTypeTests {

    /// <summary>
    /// Every built-in response type, found by reflection rather than listed, so a type added later
    /// is covered by these without anyone remembering to add it.
    /// </summary>
    public static TheoryData<Type> BuiltInResponseTypes => Carrying(typeof(IHttpStatusResponse));

    /// <summary>
    /// Every status marker, which carries the same attribute and answers a different question.
    /// </summary>
    /// <remarks>
    /// A marker is a status as a type, for <c>Status&lt;TCode, TBody&gt;</c> to close over - it is
    /// not a response and never reaches the wire, so the response invariants below do not apply to
    /// it and its own two do.
    /// </remarks>
    public static TheoryData<Type> StatusMarkers => Carrying(typeof(IStatusCode));

    /// <remarks>
    /// The contract is a <c>Type</c> rather than a type argument: <c>IStatusCode</c> declares a
    /// <c>static abstract</c> member, and an interface that does cannot be one (CS8920).
    /// </remarks>
    private static TheoryData<Type> Carrying(Type contract) {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(NotFound).Assembly.GetExportedTypes()) {
            if (type.GetCustomAttribute<HttpStatusAttribute>() != null &&
                contract.IsAssignableFrom(type)) {
                data.Add(type);
            }
        }

        return data;
    }

    /// <summary>
    /// The two sets above are the whole of what carries the attribute.
    /// </summary>
    /// <remarks>
    /// Both are found by reflection so nothing has to be remembered, and this is what keeps that
    /// true: a third kind of <c>[HttpStatus]</c> type would otherwise be covered by neither and
    /// silently untested.
    /// </remarks>
    [Fact]
    public void EveryTypeCarryingTheAttributeIsAResponseOrAMarker() {
        foreach (var type in typeof(NotFound).Assembly.GetExportedTypes()) {
            if (type.GetCustomAttribute<HttpStatusAttribute>() == null) {
                continue;
            }

            Assert.True(
                typeof(IHttpStatusResponse).IsAssignableFrom(type) ||
                typeof(IStatusCode).IsAssignableFrom(type),
                type.Name + " carries [HttpStatus] and is neither a response nor a marker.");
        }
    }

    #region status agreement

    [Theory]
    [MemberData(nameof(BuiltInResponseTypes))]
    public void HttpStatusAttribute_AgreesWithTheStatusProperty(Type type) {
        var declared = type.GetCustomAttribute<HttpStatusAttribute>()!.StatusCode;
        var instance = (IHttpStatusResponse)Instantiate(type);

        Assert.Equal(declared, instance.Status);
    }

    /// <summary>
    /// Assignability between two types in one response set has no unambiguous match order - either
    /// arm accepts the value, so which runs depends on emit order. Sealing is what removes the
    /// question, and it only works if it is true of all of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInResponseTypes))]
    public void BuiltInResponseType_IsSealed(Type type) {
        Assert.True(type.IsSealed, type.Name + " must be sealed.");
    }

    [Theory]
    [MemberData(nameof(BuiltInResponseTypes))]
    public void BuiltInResponseType_DeclaresItselfAsAStatusResponse(Type type) {
        Assert.True(
            typeof(IHttpStatusResponse).IsAssignableFrom(type),
            type.Name + " must implement IHttpStatusResponse.");
    }

    #endregion

    #region status markers

    /// <summary>
    /// The marker's own version of the agreement above, and it exists for the same reason.
    /// </summary>
    /// <remarks>
    /// A marker states its status twice for the reason <c>IStatusCode</c> documents: the generator
    /// reads attributes out of metadata and cannot evaluate a property body, and the runtime reads
    /// <c>TCode.Status</c> without reflecting. On a one-line struct the drift risk is small, and
    /// this is what makes it zero.
    /// </remarks>
    [Theory]
    [MemberData(nameof(StatusMarkers))]
    public void StatusMarker_AttributeAgreesWithTheStaticProperty(Type type) {
        var declared = type.GetCustomAttribute<HttpStatusAttribute>()!.StatusCode;

        var property = type.GetProperty(
            nameof(IStatusCode.Status), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(property);
        Assert.Equal(declared, property!.GetValue(null));
    }

    /// <summary>
    /// A marker is a type argument and nothing else, so it is a struct - which is what makes
    /// <c>TCode.Status</c> devirtualize at compile time and keeps the escape hatch AOT-safe.
    /// </summary>
    [Theory]
    [MemberData(nameof(StatusMarkers))]
    public void StatusMarker_IsAValueType(Type type) {
        Assert.True(type.IsValueType, type.Name + " must be a struct.");
    }

    /// <summary>
    /// The escape hatch itself: a status the framework ships no record for, reached by closing one
    /// generic over a marker. Two statuses are two closed types, which is the only property CS0457
    /// cares about.
    /// </summary>
    [Fact]
    public void Status_TakesItsStatusFromTheMarker() {
        Assert.Equal(418, Read(new Status<Http.ImATeapot, string>("body")));
        Assert.Equal(423, Read(new Status<Http.Locked>()));
    }

    [Fact]
    public void Status_CarriesItsBodyRatherThanItself() {
        Assert.Equal(
            "body", ((ICarriesResponseBody)new Status<Http.ImATeapot, string>("body")).Body);
    }

    [Fact]
    public void Status_WithNoBodySerializesNothing() {
        Assert.False(HasBody(new Status<Http.Locked>()));
        Assert.True(HasBody(new Status<Http.ImATeapot, string>("body")));
    }

    private static int Read(IHttpStatusResponse response) => response.Status;

    #endregion

    #region statuses

    [Fact]
    public void Statuses_AreTheOnesTheTypesAreNamedFor() {
        Assert.Equal(401, new Unauthorized().Status);
        Assert.Equal(403, new Forbidden().Status);
        Assert.Equal(404, new NotFound("todo").Status);
        Assert.Equal(409, new Conflict().Status);
        Assert.Equal(410, new Gone().Status);
        Assert.Equal(412, new PreconditionFailed().Status);
        Assert.Equal(429, new RateLimited(TimeSpan.FromSeconds(30)).Status);
        Assert.Equal(503, new ServiceUnavailable().Status);
        Assert.Equal(201, new Created<string>("v", "/things/1").Status);
        Assert.Equal(202, new Accepted().Status);
        Assert.Equal(204, new NoContent().Status);
    }

    /// <summary>
    /// A body on a 204 is not merely redundant - some clients and intermediaries reject it - and a
    /// 202 describing work that has not happened is the shape most likely to be read as a result.
    /// </summary>
    /// <remarks>
    /// Read through the interface throughout, because <c>HasBody</c> is a default interface member -
    /// only the two types that override it carry it on the type itself. That is the shape that lets
    /// a user's own response type say nothing and still mean "has a body", which is the answer for
    /// almost all of them.
    /// </remarks>
    [Fact]
    public void HasBody_IsFalseOnlyForTheBodylessStatuses() {
        Assert.False(HasBody(new NoContent()));
        Assert.False(HasBody(new Accepted()));

        Assert.True(HasBody(new NotFound("todo")));
        Assert.True(HasBody(new Conflict()));
        Assert.True(HasBody(new Created<string>("v", "/things/1")));
    }

    private static bool HasBody(IHttpStatusResponse response) => response.HasBody;

    #endregion

    #region problem type URIs

    /// <summary>
    /// RFC 9457 makes <c>type</c> the identity of a problem kind, and a client matching on it is
    /// making a branch decision. Two problems sharing a URI would silently merge those branches.
    /// </summary>
    [Fact]
    public void ProblemTypes_AreDistinctFromEachOther() {
        var uris = new[] {
            new Unauthorized().Type,
            new Forbidden().Type,
            new NotFound("todo").Type,
            new Conflict().Type,
            new Gone().Type,
            new PreconditionFailed().Type,
            new RateLimited(TimeSpan.FromSeconds(1)).Type,
            new ServiceUnavailable().Type
        };

        Assert.Equal(uris.Length, uris.Distinct().Count());
    }

    [Fact]
    public void ProblemTypes_AreAllUnderTheOnePrefix() {
        Assert.All(
            new[] {
                new Unauthorized().Type,
                new NotFound("todo").Type,
                new ServiceUnavailable().Type
            },
            uri => Assert.StartsWith(ProblemTypes.Prefix, uri, StringComparison.Ordinal));
    }

    #endregion

    /// <summary>
    /// Builds one of each with whatever its constructor asks for. Only used to read a status off an
    /// instance, so the values are placeholders and none of them is asserted on.
    /// </summary>
    private static object Instantiate(Type type) {
        var concrete = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(string)) : type;

        var constructor = concrete.GetConstructors()
            .OrderBy(c => c.GetParameters().Length)
            .First();

        var arguments = constructor.GetParameters()
            .Select(p => p.ParameterType == typeof(string)
                ? "placeholder"
                : p.ParameterType == typeof(TimeSpan)
                    ? TimeSpan.FromSeconds(1)
                    : p.HasDefaultValue
                        ? p.DefaultValue
                        : Activator.CreateInstance(p.ParameterType))
            .ToArray();

        return constructor.Invoke(arguments);
    }
}
