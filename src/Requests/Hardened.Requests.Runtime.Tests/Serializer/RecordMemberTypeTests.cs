using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// A value of the wrong type for a member a record binds through its constructor names the
/// member's type, not the record's.
/// </summary>
public class RecordMemberTypeTests
{
    public record Expense(DateOnly SpentOn, decimal Amount, int? Count);

    public record Ledger(string Name, List<Expense> Lines, Dictionary<string, Expense> ByCode);

    private static IOptions<IJsonSerializerConfiguration> Config() =>
        Options.Create<IJsonSerializerConfiguration>(
            new JsonSerializerConfiguration
            {
                DeSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            }
        );

    private static IRequestDeserializer DeserializerNamed(string name) =>
        name switch
        {
            nameof(SystemTextJsonRequestDeserializer) => new SystemTextJsonRequestDeserializer(
                Config(),
                Array.Empty<IJsonTypeInfoResolver>()
            ),
            nameof(AotRequestDeserializer) => new AotRequestDeserializer(
                Config(),
                NullLogger<AotRequestDeserializer>.Instance,
                new IJsonTypeInfoResolver[] { new DefaultJsonTypeInfoResolver() }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown deserializer"),
        };

    public static TheoryData<string> DeserializerNames =>
        new() { nameof(SystemTextJsonRequestDeserializer), nameof(AotRequestDeserializer) };

    private static async Task<RequestValidationFieldError> Refused<T>(
        string deserializerName,
        string json
    )
    {
        var context = Pipeline.Context(method: "POST", body: Encoding.UTF8.GetBytes(json));

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");

        var exception = await Assert.ThrowsAsync<JsonException>(async () =>
            await DeserializerNamed(deserializerName).DeserializeRequestBody<T>(context)
        );

        var (status, model) = new ExceptionToModelConverter().ConvertExceptionToModel(
            context,
            exception
        );

        Assert.Equal(400, status);

        return Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ADateNamesItsForm(string deserializerName)
    {
        var error = await Refused<Expense>(
            deserializerName,
            """{"spentOn":"2026-13-45","amount":1}"""
        );

        Assert.Equal("body.spentOn", error.Field);
        Assert.Equal("The value is not a date in the form YYYY-MM-DD.", error.Message);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ANumberSaysItIsANumber(string deserializerName)
    {
        var error = await Refused<Expense>(
            deserializerName,
            """{"spentOn":"2026-01-02","amount":"abc"}"""
        );

        Assert.Equal("body.amount", error.Field);
        Assert.Equal("The value is not a number this field can hold.", error.Message);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ANullableMemberNamesTheTypeItHolds(string deserializerName)
    {
        var error = await Refused<Expense>(
            deserializerName,
            """{"spentOn":"2026-01-02","amount":1,"count":"many"}"""
        );

        Assert.Equal("The value is not an integer this field can hold.", error.Message);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AMemberOfAnArrayElementNamesItsType(string deserializerName)
    {
        var error = await Refused<Ledger>(
            deserializerName,
            """{"name":"x","lines":[{"spentOn":"2026-01-02","amount":true}],"byCode":{}}"""
        );

        Assert.Equal("body.lines[0].amount", error.Field);
        Assert.Equal("The value is not a number this field can hold.", error.Message);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task AMemberOfADictionaryValueNamesItsType(string deserializerName)
    {
        var error = await Refused<Ledger>(
            deserializerName,
            """{"name":"x","lines":[],"byCode":{"a":{"spentOn":"soon","amount":1}}}"""
        );

        Assert.Equal("The value is not a date in the form YYYY-MM-DD.", error.Message);
    }

    [Theory]
    [MemberData(nameof(DeserializerNames))]
    public async Task ARecordWhereAnArrayGoesSaysSo(string deserializerName)
    {
        var error = await Refused<Ledger>(
            deserializerName,
            """{"name":"x","lines":5,"byCode":{}}"""
        );

        Assert.Equal("body.lines", error.Field);
        Assert.Equal("The value is not an array.", error.Message);
    }
}
