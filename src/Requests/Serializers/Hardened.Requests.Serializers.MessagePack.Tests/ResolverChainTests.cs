using Hardened.Requests.Serializers.MessagePack.Tests.Support;
using MessagePack;
using MessagePack.Formatters;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// Which formatters the package installs, and which it refuses to.
/// </summary>
public class ResolverChainTests {

    /// <summary>
    /// A model nobody annotated and nobody wrote a formatter for.
    /// </summary>
    /// <remarks>
    /// Its own type, separate from <see cref="Hand"/>, and that separation is the test working. A
    /// formatter declared anywhere in this assembly is found by
    /// <c>SourceGeneratedFormatterResolver</c> once it is visible enough for MessagePack's analyzer
    /// to accept it - MsgPack010 requires internal - so one type cannot be both unwritable and
    /// hand-written.
    /// </remarks>
    public record Unannotated(string Name);

    /// <summary>A model answered by a formatter this assembly declares.</summary>
    public record Hand(string Name);

    /// <summary>
    /// <b>It throws rather than reflecting.</b> The chain carries no dynamic object resolver, so a
    /// model with no <c>[MessagePackObject]</c> fails here exactly as it would after a NativeAOT
    /// publish, where <c>DynamicObjectResolver</c> cannot emit anything at all.
    /// </summary>
    /// <remarks>
    /// The same rule <c>AotResponseSerializer</c> states for JSON: a type missing from every
    /// registered context must not serialize on a developer's machine, because the developer's
    /// build is then the one configuration where the mistake is invisible.
    /// </remarks>
    [Fact]
    public async Task AModelWithNoMessagePackObjectThrows() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Unannotated("first");

        await Assert.ThrowsAsync<MessagePackSerializationException>(
            () => Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context));
    }

    /// <summary>
    /// A registered <see cref="IFormatterResolver"/> is asked first, which is how an application
    /// answers for something the generator wrote no formatter for. The JSON side does the same with
    /// <c>IJsonTypeInfoResolver</c>.
    /// </summary>
    [Fact]
    public async Task ARegisteredResolverAnswersForATypeTheGeneratorDidNotWrite() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Hand("first");

        await Pipeline
            .ResponseSerializer(Pipeline.Pool(), Pipeline.Options(null, HandResolver.Instance))
            .SerializeResponse(context);

        Assert.Equal(
            MessagePackSerializer.Serialize(
                "first", MessagePackSerializerOptions.Standard,
                TestContext.Current.CancellationToken),
            Pipeline.BodyOf(context));
    }

    /// <summary>
    /// Internal rather than private, and over the nullable type: MsgPack010 and MsgPack014, which
    /// this package's analyzer reports and a CI build turns into errors.
    /// </summary>
    internal sealed class HandResolver : IFormatterResolver {
        public static readonly HandResolver Instance = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(Hand) ? (IMessagePackFormatter<T>)(object)HandFormatter.Instance : null;
    }

    internal sealed class HandFormatter : IMessagePackFormatter<Hand?> {
        public static readonly HandFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer, Hand? value, MessagePackSerializerOptions options) =>
            writer.Write(value?.Name);

        public Hand? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) =>
            reader.ReadString() is { } name ? new Hand(name) : null;
    }
}
