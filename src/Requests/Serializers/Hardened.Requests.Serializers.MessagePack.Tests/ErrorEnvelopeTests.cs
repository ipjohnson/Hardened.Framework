using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Serializers.MessagePack.Tests.Support;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// The framework's error envelopes, out through the pipeline and back.
/// </summary>
/// <remarks>
/// <para>
/// A refusal on an operation declaring <c>application/x-msgpack</c> answered <b>500 with an empty
/// body</b> before these formatters existed. <c>ExceptionResponseSerializer</c> negotiates the
/// error body through the same locator the success goes through, so the MessagePack writer was
/// chosen, found no formatter for <c>RequestValidationError</c> - a type in
/// <c>Hardened.Requests.Runtime</c>, which is never going to reference MessagePack - and threw
/// inside the handler whose job is answering a throw. Every bind failure, validation failure and
/// authorization refusal on such an operation went out that way.
/// </para>
/// <para>
/// Read back with the resolver rather than with <c>StandardResolver</c>, which is how a client
/// reads one: the formatters are hand-written, so nothing finds them by convention.
/// </para>
/// </remarks>
public class ErrorEnvelopeTests
{
    private static readonly MessagePackSerializerOptions ClientOptions =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                [],
                [HardenedFormatterResolver.Instance, StandardResolver.Instance]
            )
        );

    private static async Task<T?> RoundTrip<T>(T value)
    {
        var context = Pipeline.Context();

        context.Response.ResponseValue = value;

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        return MessagePackSerializer.Deserialize<T>(
            Pipeline.BodyOf(context),
            ClientOptions,
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task AnErrorModelRoundTrips()
    {
        var read = await RoundTrip(ErrorModel.For(409, "Nope."));

        Assert.NotNull(read);
        Assert.Equal("urn:hardened:problem:conflict", read.Type);
        Assert.Equal("Conflict", read.Title);
        Assert.Equal(409, read.Status);
        Assert.Equal("Nope.", read.Detail);
    }

    [Fact]
    public async Task AValidationErrorRoundTripsWithItsFields()
    {
        var read = await RoundTrip(
            new RequestValidationError
            {
                Status = 422,
                Errors =
                {
                    new RequestValidationFieldError
                    {
                        Field = "id",
                        Code = "range",
                        Message = "Too low.",
                    },
                    new RequestValidationFieldError
                    {
                        Field = "title",
                        Code = "length",
                        Message = "Too long.",
                    },
                },
            }
        );

        Assert.NotNull(read);
        Assert.Equal(RequestValidationError.ProblemType, read.Type);
        Assert.Equal(422, read.Status);
        Assert.Equal("One or more validation errors occurred.", read.Detail);
        Assert.Equal(2, read.Errors.Count);
        Assert.Equal("id", read.Errors[0].Field);
        Assert.Equal("length", read.Errors[1].Code);
        Assert.Equal("Too long.", read.Errors[1].Message);
    }

    /// <summary>
    /// An envelope with no field errors, which is every refusal that is not a validation failure.
    /// </summary>
    [Fact]
    public async Task AValidationErrorWithNoFieldsRoundTrips()
    {
        var read = await RoundTrip(new RequestValidationError { Detail = "No." });

        Assert.NotNull(read);
        Assert.Empty(read.Errors);
    }

    /// <summary>
    /// Map style with the keys the JSON representation uses, so a caller switching on
    /// <c>type</c> reads the same problem document either way.
    /// </summary>
    [Fact]
    public void TheKeysAreTheJsonPropertyNames()
    {
        var json = MessagePackSerializer.ConvertToJson(
            MessagePackSerializer.Serialize(
                ErrorModel.For(409, "Nope."),
                ClientOptions,
                TestContext.Current.CancellationToken
            ),
            ClientOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            "{\"detail\":\"Nope.\",\"type\":\"urn:hardened:problem:conflict\",\"title\":\"Conflict\",\"status\":409}",
            json
        );
    }

    /// <summary>
    /// A member a newer framework added is skipped rather than refused. An envelope is not worth a
    /// break between a client and a server one version apart.
    /// </summary>
    [Fact]
    public void AnUnknownMemberIsSkipped()
    {
        var bytes = MessagePackSerializer.ConvertFromJson(
            "{\"type\":\"about:blank\",\"detail\":\"d\",\"instance\":\"/x\"}",
            ClientOptions,
            TestContext.Current.CancellationToken
        );

        var read = MessagePackSerializer.Deserialize<ErrorModel>(
            bytes,
            ClientOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(read);
        Assert.Equal("about:blank", read.Type);
        Assert.Equal("d", read.Detail);
    }

    /// <summary>
    /// A nil reads as an empty envelope rather than throwing, which is what the JSON side does with
    /// <c>null</c>.
    /// </summary>
    [Fact]
    public void ANilReadsAsAnEmptyEnvelope()
    {
        var read = MessagePackSerializer.Deserialize<ErrorModel>(
            MessagePackSerializer.ConvertFromJson(
                "null",
                ClientOptions,
                TestContext.Current.CancellationToken
            ),
            ClientOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(read);
        Assert.Equal("about:blank", read.Type);
    }
}
