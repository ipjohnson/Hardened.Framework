using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Serializer;
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

    [Fact]
    public void ParseRequired_ThrowsBadRequestException_ForEmptyValue() {
        Assert.Throws<BadRequestException>(() =>
            _service.ParseRequired<int>("", "testValue"));
    }

    [Fact]
    public void ParseRequired_ThrowsBadRequestException_ForNullValue() {
        Assert.Throws<BadRequestException>(() =>
            _service.ParseRequired<int>(null!, "testValue"));
    }

    [Fact]
    public void ParseRequired_ThrowsBadRequestException_ForMalformedValue() {
        var ex = Assert.Throws<BadRequestException>(() =>
            _service.ParseRequired<int>("not-a-number", "testValue"));
        Assert.Contains("malformed", ex.Message);
    }

    [Fact]
    public void ParseWithDefault_ReturnsDefault_ForEmptyString() {
        var result = _service.ParseWithDefault("", "testValue", 99);
        Assert.Equal(99, result);
    }

    [Fact]
    public void ParseWithDefault_ReturnsDefault_ForMalformedValue() {
        var result = _service.ParseWithDefault("not-a-number", "testValue", 99);
        Assert.Equal(99, result);
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

    [Fact]
    public void ParseOptional_ReturnsNull_ForMalformedValue() {
        var result = _service.ParseOptional<int>("not-a-number", "testValue");
        Assert.Equal(default, result);
    }

    [Fact]
    public void ParseOptional_ReturnsParsedValue_WhenValid() {
        var result = _service.ParseOptional<int>("42", "testValue");
        Assert.Equal(42, result);
    }

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
}
