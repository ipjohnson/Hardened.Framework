using Hardened.Requests.Serializers.MessagePack.Tests.Support;
using MessagePack;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// The request path reads untrusted bytes, so it reads them with <c>UntrustedData</c>. Standard
/// (TrustedData) applies no object-graph depth limit, and a small, deeply nested body would recurse
/// into a <c>StackOverflowException</c> the runtime cannot catch, taking the process down.
/// </summary>
public class RequestBodySecurityTests
{
    /// <summary>
    /// A body of nested single-element arrays. Each <c>0x91</c> is a one-element fixarray header, so
    /// <paramref name="depth"/> of them nest that deep before the terminal value.
    /// </summary>
    private static byte[] NestedArrays(int depth)
    {
        var bytes = new byte[depth + 1];

        for (var i = 0; i < depth; i++)
        {
            bytes[i] = 0x91;
        }

        bytes[depth] = 0x00; // the innermost value: positive fixint 0

        return bytes;
    }

    [Fact]
    public async Task ADeeplyNestedBodyIsRefusedRatherThanOverflowingTheStack()
    {
        // Past UntrustedData's default MaximumObjectGraphDepth of 500. Under TrustedData this same
        // body just keeps recursing.
        var body = NestedArrays(2000);

        await Assert.ThrowsAsync<MessagePackSerializationException>(async () =>
            await Pipeline
                .Deserializer(Pipeline.Pool())
                .DeserializeRequestBody<object>(Pipeline.Context(body))
        );
    }

    [Fact]
    public void TheReadPathUsesUntrustedDataByDefault()
    {
        Assert.Same(MessagePackSecurity.UntrustedData, Pipeline.Options().ReadOptions.Security);
    }

    [Fact]
    public void TheWritePathKeepsTheStandardSecurity()
    {
        Assert.Same(MessagePackSecurity.TrustedData, Pipeline.Options().Options.Security);
    }

    [Fact]
    public void AnApplicationsOwnSecurityChoiceIsKeptOnTheReadPath()
    {
        // A provider that pins a non-default security. The read path must not override it.
        var options = Pipeline.Options(provider: _ =>
            MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData)
        );

        Assert.Same(MessagePackSecurity.UntrustedData, options.ReadOptions.Security);
        Assert.Same(options.Options, options.ReadOptions);
    }
}
