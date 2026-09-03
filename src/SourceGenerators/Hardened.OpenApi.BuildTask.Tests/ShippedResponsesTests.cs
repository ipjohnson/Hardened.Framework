using Hardened.Generation;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Which declared errors bind to a type the framework ships, and which still get one generated.
/// </summary>
/// <remarks>
/// <para>
/// The decision the whole change rests on, and the reason it is one function: four separate things
/// have to reach the same answer from it - the build task that writes the types, the Roslyn
/// generator that writes the switch over them, the response-set plan, and the name allocator. The
/// generator never sees the task's output, so a second derivation of this is a switch arm naming a
/// type nothing emitted.
/// </para>
/// <para>
/// What the table holds is asserted only where the answer is load-bearing. Every status having a
/// row is not the property worth pinning; the three reasons a type still has to be generated are.
/// </para>
/// </remarks>
public class ShippedResponsesTests {

    private static ErrorResponseModel Error(
        int statusCode, string? bodyRef = null, string? name = null,
        params string[] headers) {
        var error = new ErrorResponseModel {
            StatusCode = statusCode,
            Ref = bodyRef,
            Name = name
        };

        foreach (var header in headers) {
            error.Headers.Add(new ResponseHeaderModel { Name = header, ParameterName = header });
        }

        return error;
    }

    #region what binds

    /// <summary>
    /// The ordinary case, and the one that takes the petstore fixture's four generated types to
    /// none: a declared status with a body, over a schema, at a status the framework ships a
    /// generic record for.
    /// </summary>
    [Theory]
    [InlineData(400, "BadRequest")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "NotFound")]
    [InlineData(409, "Conflict")]
    [InlineData(422, "UnprocessableContent")]
    [InlineData(429, "RateLimited")]
    [InlineData(500, "InternalServerError")]
    public void ADeclaredBodyBindsToTheGenericShippedRecord(int statusCode, string expected) {
        var binding = ShippedResponses.For(Error(statusCode, "#/components/schemas/Problem"));

        Assert.NotNull(binding);
        Assert.Equal(expected, binding!.Value.TypeName);
        Assert.True(binding.Value.TakesBody);
        Assert.Null(binding.Value.Marker);
    }

    /// <summary>
    /// A declared response with no content. It binds to the bare form, which carries the
    /// framework's own problem document rather than the declared schema - because there is no
    /// declared schema, and what this replaced was an <c>ErrorModel</c> naming a generated
    /// exception class.
    /// </summary>
    [Fact]
    public void ADeclaredResponseWithNoContentBindsToTheBareForm() {
        var binding = ShippedResponses.For(Error(403));

        Assert.NotNull(binding);
        Assert.Equal("Forbidden", binding!.Value.TypeName);
        Assert.False(binding.Value.TakesBody);
    }

    /// <summary>
    /// The two statuses the framework refuses to put a body on, which is why neither has a generic
    /// form and why the bound case reports no body.
    /// </summary>
    [Theory]
    [InlineData(304, "NotModified")]
    [InlineData(406, "NotAcceptable")]
    public void ABodylessStatusBindsAndSerializesNothing(int statusCode, string expected) {
        var binding = ShippedResponses.For(Error(statusCode));

        Assert.NotNull(binding);
        Assert.Equal(expected, binding!.Value.TypeName);
        Assert.False(binding.Value.HasBody);
    }

    /// <summary>
    /// The escape hatch. A registered status with no record of its own closes the one generic over
    /// a marker, so the framework costs a line per status instead of a record - and two statuses
    /// are still two types, which is the only property CS0457 cares about.
    /// </summary>
    [Theory]
    [InlineData(418, "ImATeapot")]
    [InlineData(423, "Locked")]
    [InlineData(451, "UnavailableForLegalReasons")]
    [InlineData(507, "InsufficientStorage")]
    public void AStatusWithNoRecordBindsToAClosedStatusGeneric(int statusCode, string marker) {
        var binding = ShippedResponses.For(Error(statusCode, "#/components/schemas/Problem"));

        Assert.NotNull(binding);
        Assert.Equal("Status", binding!.Value.TypeName);
        Assert.Equal(marker, binding.Value.Marker);
        Assert.True(binding.Value.TakesBody);
    }

    /// <summary>
    /// The statuses whose response is not well formed without a header. Read off the table so the
    /// emitted switch calls <c>ApplyHeaders</c> for exactly those arms rather than type-testing
    /// every response at run time.
    /// </summary>
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public void AShippedRecordThatWritesAHeaderSaysSo(int statusCode) {
        Assert.True(
            ShippedResponses.For(Error(statusCode, "#/components/schemas/Problem"))!
                .Value.AppliesHeaders);
    }

    /// <summary>
    /// 304's <c>ETag</c>, which is on the bare form because there is no other form: the status
    /// forbids a body, so nothing binds a declared 304 that names a schema.
    /// </summary>
    [Fact]
    public void NotModifiedWritesItsETag() {
        Assert.True(ShippedResponses.For(Error(304))!.Value.AppliesHeaders);
        Assert.Null(ShippedResponses.For(Error(304, "#/components/schemas/Problem")));
    }

    [Fact]
    public void AShippedRecordWithNoHeaderOfItsOwnSaysSo() {
        Assert.False(
            ShippedResponses.For(Error(404, "#/components/schemas/Problem"))!
                .Value.AppliesHeaders);
    }

    #endregion

    #region what does not

    /// <summary>
    /// The first of the three reasons a type is still generated, and the one option B exists for.
    /// A Smithy error is a named shape and no shipped record can carry that name.
    /// </summary>
    [Fact]
    public void AnErrorTheDescriptionNamedIsNotBound() {
        Assert.Null(
            ShippedResponses.For(
                Error(400, "#/components/schemas/AccountNotFound", name: "AccountNotFound")));
    }

    /// <summary>
    /// The second. <c>NotFound&lt;T&gt;</c> has nowhere to put a declared <c>Retry-After</c>, and
    /// a header the document declares and nothing sends is worse than an extra type.
    /// </summary>
    [Fact]
    public void AnErrorDeclaringAHeaderIsNotBound() {
        Assert.Null(
            ShippedResponses.For(
                Error(429, "#/components/schemas/Problem", null, "Retry-After")));
    }

    /// <summary>
    /// The third, and the one a longer table can never answer. 529 is registered nowhere -
    /// Anthropic's API returns it for an overloaded server and no RFC defines it - so it has
    /// neither a record nor a marker.
    /// </summary>
    [Theory]
    [InlineData(529)]
    [InlineData(599)]
    public void AnUnregisteredStatusIsNotBound(int statusCode) {
        Assert.Null(ShippedResponses.For(Error(statusCode, "#/components/schemas/Problem")));
    }

    #endregion

    #region generated names

    /// <summary>
    /// The declared name where there is one, which is what keying by identity means.
    /// </summary>
    [Fact]
    public void ANamedErrorWantsItsOwnName() {
        Assert.Equal(
            "AccountNotFound",
            ShippedResponses.GeneratedName(
                Error(400, "#/components/schemas/AccountNotFound", name: "AccountNotFound")));
    }

    /// <summary>
    /// Otherwise the status and the payload schema, which is <c>DefaultErrorBody.FieldName</c>'s
    /// existing scheme. Not the operation: two operations declaring one 404 over one schema want
    /// one type.
    /// </summary>
    [Fact]
    public void AnAnonymousErrorWantsTheStatusAndItsSchema() {
        Assert.Equal(
            "NotFoundProblem",
            ShippedResponses.GeneratedName(Error(404, "#/components/schemas/Problem")));
    }

    [Fact]
    public void AnAnonymousErrorWithNoBodyWantsJustTheStatus() {
        Assert.Equal("Status529", ShippedResponses.GeneratedName(Error(529)));
    }

    /// <summary>
    /// Two errors wanting one name over different payloads are two types. Keyed on the payload as
    /// well as the name for that reason - collapsing them by name would emit one record and
    /// reference it for both.
    /// </summary>
    [Fact]
    public void TwoErrorsWithOneNameAndDifferentPayloadsAreDifferentKeys() {
        Assert.NotEqual(
            ShippedResponses.GeneratedKey(Error(529, "#/components/schemas/Problem")),
            ShippedResponses.GeneratedKey(Error(529, "#/components/schemas/ApiError")));
    }

    /// <summary>
    /// And two declarations of the same error are one key, which is what makes the emitted set
    /// distinct rather than one entry per operation.
    /// </summary>
    [Fact]
    public void TwoDeclarationsOfOneErrorAreOneKey() {
        Assert.Equal(
            ShippedResponses.GeneratedKey(Error(529, "#/components/schemas/Problem")),
            ShippedResponses.GeneratedKey(Error(529, "#/components/schemas/Problem")));
    }

    /// <summary>
    /// A 429 with a declared <c>Retry-After</c> and one without are different constructors, so
    /// they are different types.
    /// </summary>
    [Fact]
    public void ADeclaredHeaderIsPartOfTheKey() {
        Assert.NotEqual(
            ShippedResponses.GeneratedKey(Error(529, "#/components/schemas/Problem")),
            ShippedResponses.GeneratedKey(
                Error(529, "#/components/schemas/Problem", null, "Retry-After")));
    }

    #endregion

    #region the tables as a whole

    /// <summary>
    /// No status has both a shipped record and a marker.
    /// </summary>
    /// <remarks>
    /// The property the two tables have to hold jointly, and the one that is easy to break by
    /// adding a record and forgetting to remove the marker. <c>BareForm</c> is consulted first, so
    /// a status in both would silently make its marker unreachable - and an application already
    /// writing <c>Status&lt;Http.X, T&gt;</c> would keep compiling while the build stopped
    /// generating that shape.
    /// </remarks>
    [Fact]
    public void NoStatusHasBothARecordAndAMarker() {
        for (var status = 100; status < 600; status++) {
            if (ShippedResponses.BareForm(status) != null) {
                Assert.Null(ShippedResponses.Marker(status));
            }
        }
    }

    /// <summary>
    /// A generic form always has a bare form beside it, never the reverse.
    /// </summary>
    /// <remarks>
    /// 304 and 406 are the two that are bare only, because the framework refuses to put a body on
    /// either. A generic form with no bare form would be a status that can carry a declared payload
    /// and cannot carry the framework's own problem document, which is not a distinction anything
    /// here makes.
    /// </remarks>
    [Fact]
    public void EveryGenericFormHasABareForm() {
        for (var status = 100; status < 600; status++) {
            if (ShippedResponses.GenericForm(status) != null) {
                Assert.NotNull(ShippedResponses.BareForm(status));
            }
        }
    }

    /// <summary>
    /// A declared error binds if and only if its status has a record or a marker.
    /// </summary>
    /// <remarks>
    /// Swept rather than sampled, because this is what decides whether a type is generated and the
    /// tables are long enough that a wrong row would not be noticed. The three reasons a bound
    /// status still generates are asserted above.
    /// </remarks>
    [Fact]
    public void BindingFollowsTheTables() {
        for (var status = 100; status < 600; status++) {
            var hasSomething =
                ShippedResponses.BareForm(status) != null ||
                ShippedResponses.Marker(status) != null;

            Assert.Equal(hasSomething, ShippedResponses.For(Error(status)) != null);
        }
    }

    /// <summary>
    /// Every status names a C# identifier, including the ones no table lists.
    /// </summary>
    /// <remarks>
    /// A generated type is named from this, so a row that produced a leading digit or a space would
    /// be a compiler error in generated code rather than a bad name.
    /// </remarks>
    [Fact]
    public void EveryStatusNameIsAnIdentifier() {
        for (var status = 100; status < 600; status++) {
            var name = ShippedResponses.StatusName(status);

            Assert.NotEmpty(name);
            Assert.True(char.IsLetter(name[0]), name);
            Assert.All(name, character => Assert.True(char.IsLetterOrDigit(character), name));
        }
    }

    #endregion

    #region the one status table

    /// <summary>
    /// The punch-list items, as assertions. Two tables used to name generated types and had
    /// drifted: 422 was <c>UnprocessableEntity</c> in one and <c>UnprocessableContent</c> in the
    /// other, 413 was <c>PayloadTooLarge</c> in one and absent from the other, 428 was in one only.
    /// A generated name is API, so one status has one name.
    /// </summary>
    [Theory]
    [InlineData(413, "ContentTooLarge")]
    [InlineData(422, "UnprocessableContent")]
    [InlineData(428, "PreconditionRequired")]
    public void TheStatusNameIsRfc9110s(int statusCode, string expected) {
        Assert.Equal(expected, ShippedResponses.StatusName(statusCode));
    }

    /// <summary>
    /// And where the framework ships a record, the generated name is that record's - so a residual
    /// type and the framework type beside it read the same. This is what settles 429, which the
    /// generators called <c>TooManyRequests</c> and the framework calls <c>RateLimited</c>.
    /// </summary>
    [Fact]
    public void TheStatusNameIsTheShippedRecordsWhereThereIsOne() {
        Assert.Equal("RateLimited", ShippedResponses.StatusName(429));
    }

    [Fact]
    public void AStatusWithNoRegisteredNameKeepsItsNumber() {
        Assert.Equal("Status529", ShippedResponses.StatusName(529));
    }

    #endregion
}
