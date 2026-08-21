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
    /// Every built-in type, found by reflection rather than listed, so a type added later is
    /// covered by these without anyone remembering to add it.
    /// </summary>
    public static TheoryData<Type> BuiltInResponseTypes {
        get {
            var data = new TheoryData<Type>();

            foreach (var type in typeof(NotFound).Assembly.GetExportedTypes()) {
                if (type.GetCustomAttribute<HttpStatusAttribute>() != null) {
                    data.Add(type);
                }
            }

            return data;
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
