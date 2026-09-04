using System.Globalization;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

public class StringConverterServiceTests {
    private readonly StringConverterService _service;

    public StringConverterServiceTests() {
        _service = new StringConverterService(Array.Empty<IStringConverter>());
    }

    [Fact]
    public void ParseRequired_ReturnsParsedInt() {
        var result = _service.ParseRequired<int>("42", "testValue");
        Assert.Equal(42, result);
    }

    [Fact]
    public void ParseRequired_ReturnsParsedLong() {
        var result = _service.ParseRequired<long>("9999999999", "testValue");
        Assert.Equal(9999999999L, result);
    }

    [Fact]
    public void ParseRequired_ReturnsParsedGuid() {
        var guid = Guid.NewGuid();
        var result = _service.ParseRequired<Guid>(guid.ToString(), "testValue");
        Assert.Equal(guid, result);
    }

    [Fact]
    public void ParseRequired_ReturnsParsedDateTime() {
        var result = _service.ParseRequired<DateTime>("2024-01-15", "testValue");
        Assert.Equal(new DateTime(2024, 1, 15), result);
    }

    [Fact]
    public void ParseRequired_ReturnsParsedString() {
        var result = _service.ParseRequired<string>("hello", "testValue");
        Assert.Equal("hello", result);
    }

    /// <summary>
    /// A missing required value reports under the same code a missing required property does, so a
    /// caller handling "you left something out" handles both without knowing that one came from the
    /// URL and the other from the body.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseRequired_Throws_WhenValueIsAbsent(string? value) {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseRequired<int>(value!, "testValue"));

        var error = Assert.Single(ex.ValidationResult.Errors);
        Assert.Equal("testValue", error.Field);
        Assert.Equal(ValidationModules.ValidationCodes.Required, error.Code);
    }

    [Fact]
    public void ParseRequired_ThrowsBadRequestException_ForMalformedValue() {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseRequired<int>("not-a-number", "testValue"));

        var error = Assert.Single(ex.ValidationResult.Errors);
        Assert.Equal("testValue", error.Field);
        Assert.Equal("invalid", error.Code);
    }

    [Fact]
    public void ParseWithDefault_ReturnsDefault_ForEmptyString() {
        var result = _service.ParseWithDefault("", "testValue", 99);
        Assert.Equal(99, result);
    }

    /// <summary>
    /// A default covers a value that was not sent, not one that was sent and made no sense. The
    /// second is a mistake the caller can fix, and quietly substituting a number they did not choose
    /// hides it.
    /// </summary>
    [Fact]
    public void ParseWithDefault_Throws_ForMalformedValue() {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseWithDefault("not-a-number", "testValue", 99));

        Assert.Equal("invalid", Assert.Single(ex.ValidationResult.Errors).Code);
    }

    [Fact]
    public void ParseWithDefault_ReturnsParsedValue_WhenValid() {
        var result = _service.ParseWithDefault("42", "testValue", 99);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ParseOptional_ReturnsNull_ForEmptyString() {
        var result = _service.ParseOptional<int>("", "testValue");
        Assert.Equal(default, result);
    }

    /// <summary>
    /// Optional means the caller may omit it, not that anything they send is acceptable. Returning
    /// null here made ?limit=abc and no limit at all indistinguishable, so the request went through
    /// with the parameter unset and every constraint on it silently unevaluated.
    /// </summary>
    [Fact]
    public void ParseOptional_Throws_ForMalformedValue() {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseOptional<int>("not-a-number", "testValue"));

        Assert.Equal("invalid", Assert.Single(ex.ValidationResult.Errors).Code);
    }

    /// <summary>A 400 rather than a 500: the exception still carries its client-error lineage.</summary>
    [Fact]
    public void AParseFailureIsAClientError() {
        Assert.IsAssignableFrom<BadRequestException>(
            Assert.ThrowsAny<Exception>(() => _service.ParseOptional<int>("nope", "testValue")));
    }

    [Fact]
    public void ParseOptional_ReturnsParsedValue_WhenValid() {
        var result = _service.ParseOptional<int>("42", "testValue");
        Assert.Equal(42, result);
    }

    #region collections

    /// <summary>
    /// One entry per repeat, which is how OpenAPI's default array style and a repeated header line
    /// both arrive.
    /// </summary>
    [Fact]
    public void ParseOptionalMany_ReadsOneItemPerValue() {
        Assert.Equal(
            ["EUR", "GBP"],
            _service.ParseOptionalMany<string>(new StringValues(["EUR", "GBP"]), "symbols"));
    }

    /// <summary>
    /// And the joined spelling, which is <c>explode: false</c> and what RFC 9110 lets a recipient
    /// make of repeated header lines. Nothing in the model says which one a contract asked for, so
    /// both are read.
    /// </summary>
    [Fact]
    public void ParseOptionalMany_SplitsAJoinedValue() {
        Assert.Equal(
            ["EUR", "GBP"], _service.ParseOptionalMany<string>("EUR,GBP", "symbols"));
    }

    [Fact]
    public void ParseOptionalMany_TrimsAroundTheSeparator() {
        Assert.Equal(
            ["EUR", "GBP"], _service.ParseOptionalMany<string>("EUR, GBP", "symbols"));
    }

    [Fact]
    public void ParseOptionalMany_CombinesBothSpellings() {
        Assert.Equal(
            [1, 2, 3], _service.ParseOptionalMany<int>(new StringValues(["1", "2,3"]), "ids"));
    }

    [Fact]
    public void ParseOptionalMany_ConvertsEachItem() {
        Assert.Equal([1, 2], _service.ParseOptionalMany<int>("1,2", "ids"));
    }

    /// <summary>
    /// Absent is null rather than an empty list, so a handler can tell "sent nothing" from "sent an
    /// empty list" - the distinction ParseOptional draws for every other type.
    /// </summary>
    [Fact]
    public void ParseOptionalMany_ReturnsNull_WhenNothingWasSent() {
        Assert.Null(_service.ParseOptionalMany<string>(StringValues.Empty, "symbols"));
    }

    /// <summary>
    /// A hole is not an item. <c>?ids=1&amp;ids=&amp;ids=3</c> is two ids, and a lone <c>?ids=</c>
    /// is the empty list rather than a list containing one unparseable thing.
    /// </summary>
    [Fact]
    public void ParseOptionalMany_DropsEmptyEntries() {
        Assert.Equal([1, 3], _service.ParseOptionalMany<int>(new StringValues(["1", "", "3"]), "ids"));
    }

    [Fact]
    public void ParseOptionalMany_ReturnsTheEmptyList_ForAnEmptyValue() {
        Assert.Empty(_service.ParseOptionalMany<int>(new StringValues(""), "ids")!);
    }

    /// <summary>An item that will not convert fails the request, as a scalar one does.</summary>
    [Fact]
    public void ParseOptionalMany_Throws_ForAMalformedItem() {
        var exception = Assert.Throws<ValidationException>(
            () => _service.ParseOptionalMany<int>("1,abc", "ids"));

        Assert.Equal("invalid", Assert.Single(exception.ValidationResult.Errors).Code);
    }

    /// <summary>Named by the parameter, not by the item, so the error points at what the caller sent.</summary>
    [Fact]
    public void AMalformedItemIsReportedAgainstTheParameter() {
        var exception = Assert.Throws<ValidationException>(
            () => _service.ParseOptionalMany<int>("1,abc", "ids"));

        Assert.Equal("ids", Assert.Single(exception.ValidationResult.Errors).Field);
    }

    [Fact]
    public void ParseRequiredMany_ReadsTheItems() {
        Assert.Equal(["EUR", "GBP"], _service.ParseRequiredMany<string>("EUR,GBP", "symbols"));
    }

    [Fact]
    public void ParseRequiredMany_Throws_WhenNothingWasSent() {
        var exception = Assert.Throws<ValidationException>(
            () => _service.ParseRequiredMany<string>(StringValues.Empty, "symbols"));

        Assert.Equal("required", Assert.Single(exception.ValidationResult.Errors).Code);
    }

    /// <summary>
    /// A parameter that arrived with nothing in it is as absent as one that did not arrive.
    /// </summary>
    [Fact]
    public void ParseRequiredMany_Throws_WhenEveryEntryIsEmpty() {
        Assert.Throws<ValidationException>(
            () => _service.ParseRequiredMany<string>(new StringValues(["", ""]), "symbols"));
    }

    #endregion

    [Fact]
    public void CustomStringConverter_IsUsedWhenRegistered() {
        var converter = Substitute.For<IStringConverter>();
        converter.ConvertType.Returns(typeof(int));
        converter.Convert<int>("custom-42").Returns(42);

        var service = new StringConverterService(new[] { converter });

        var result = service.ParseRequired<int>("custom-42", "testValue");

        Assert.Equal(42, result);
        converter.Received(1).Convert<int>("custom-42");
    }

    /// <summary>
    /// The types a generated binder can ask for, in one place.
    /// </summary>
    /// <remarks>
    /// Every one of these used to throw, and optional parsing swallowed the throw - so a spec
    /// declaring <c>format: date</c> produced a parameter that bound as null however the caller
    /// filled it in. Enumerated rather than sampled because the failure mode is a gap in a table,
    /// and a sample cannot show a gap.
    /// </remarks>
    [Theory]
    [InlineData(typeof(bool), "true")]
    [InlineData(typeof(byte), "7")]
    [InlineData(typeof(sbyte), "-7")]
    [InlineData(typeof(short), "-300")]
    [InlineData(typeof(ushort), "300")]
    [InlineData(typeof(int), "42")]
    [InlineData(typeof(uint), "42")]
    [InlineData(typeof(long), "9999999999")]
    [InlineData(typeof(ulong), "9999999999")]
    [InlineData(typeof(float), "1.5")]
    [InlineData(typeof(double), "1.5")]
    [InlineData(typeof(decimal), "1.5")]
    [InlineData(typeof(char), "x")]
    [InlineData(typeof(string), "hello")]
    [InlineData(typeof(DateTime), "2024-01-15")]
    [InlineData(typeof(DateTimeOffset), "2024-01-15T10:30:00Z")]
    [InlineData(typeof(DateOnly), "2024-01-15")]
    [InlineData(typeof(TimeOnly), "10:30:00")]
    [InlineData(typeof(TimeSpan), "01:30:00")]
    [InlineData(typeof(Guid), "8a1b0c9d-0000-0000-0000-000000000000")]
    [InlineData(typeof(Uri), "https://example.com/pets")]
    public void EveryDeclarableTypeConverts(Type type, string value) {
        Assert.NotNull(Invoke(nameof(IStringConverterService.ParseRequired), type, value));
    }

    /// <summary>
    /// The same table serves the nullable form. Two lists would drift, and the one that drifted last
    /// time was this one - <c>Nullable&lt;int&gt;</c> matched nothing, so every optional value-type
    /// parameter bound as null whatever the caller sent.
    /// </summary>
    [Theory]
    [InlineData(typeof(int?), "42")]
    [InlineData(typeof(bool?), "true")]
    [InlineData(typeof(double?), "1.5")]
    [InlineData(typeof(DateOnly?), "2024-01-15")]
    [InlineData(typeof(Guid?), "8a1b0c9d-0000-0000-0000-000000000000")]
    public void NullableFormsConvertToo(Type type, string value) {
        Assert.NotNull(Invoke(nameof(IStringConverterService.ParseOptional), type, value));
    }

    [Fact]
    public void EnumsParseByNameAndIgnoreCase() {
        Assert.Equal(PetStatus.Available, _service.ParseRequired<PetStatus>("AVAILABLE", "status"));
    }

    /// <summary>
    /// Base64, because that is how a specification carries <c>format: byte</c> through a string
    /// position.
    /// </summary>
    [Fact]
    public void ByteArraysArriveAsBase64() {
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            _service.ParseRequired<byte[]>(Convert.ToBase64String(new byte[] { 1, 2, 3 }), "data"));
    }

    /// <summary>
    /// Wire values are not user input. <c>1.5</c> is one and a half wherever the server runs, and
    /// under a comma-decimal culture the machine default would have read it as fifteen.
    /// </summary>
    [Fact]
    public void ParsingDoesNotFollowTheAmbientCulture() {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try {
            Assert.Equal(1.5m, _service.ParseRequired<decimal>("1.5", "price"));
        }
        finally {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A type nothing can parse still reports as a client error rather than escaping as a 500.</summary>
    [Fact]
    public void AnUnsupportedTypeReportsAsInvalid() {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseRequired<StringConverterServiceTests>("anything", "testValue"));

        Assert.Equal(ValidationModules.ValidationCodes.Invalid,
            Assert.Single(ex.ValidationResult.Errors).Code);
    }

    /// <summary>
    /// The reason the value would not convert survives on the exception. The caller is told the
    /// field is invalid and needs nothing more; a log with "abc is not in a correct format" beats one
    /// asserting only that something was wrong.
    /// </summary>
    [Fact]
    public void TheUnderlyingParseFailureIsKept() {
        var ex = Assert.Throws<ValidationException>(() =>
            _service.ParseRequired<int>("not-a-number", "testValue"));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    public enum PetStatus { Available, Pending, Sold }

    private object? Invoke(string method, Type type, string value) =>
        typeof(StringConverterService)
            .GetMethod(method)!
            .MakeGenericMethod(type)
            .Invoke(_service, new object?[] { value, "testValue" });
}
