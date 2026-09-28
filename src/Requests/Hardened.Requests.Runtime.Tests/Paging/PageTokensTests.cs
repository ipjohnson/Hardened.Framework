using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Paging;
using Hardened.Requests.Runtime.Paging;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Validation;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Paging;

/// <summary>A cursor only a source-generated context describes, for the AOT serializer.</summary>
public record ContextCursor(DateTimeOffset CreatedAt, int Id);

// At namespace scope, because System.Text.Json's generator does not emit for a context nested in a
// type that is not partial.
[JsonSerializable(typeof(ContextCursor))]
internal partial class PageCursorContext : JsonSerializerContext;

public class PageTokensTests
{
    private sealed record StaffCursor(DateTimeOffset CreatedAt, int Id);

    private static readonly StaffCursor Cursor = new(
        new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero),
        42
    );

    /// <summary>Typed as the interface, which is where the compiler reads the argument's name from.</summary>
    private static IPageTokens Tokens(string? key = null) =>
        new PageTokens(
            new JsonSerializerImpl(
                Options.Create<ISharedJsonConfiguration>(new SharedJsonConfiguration()),
                Array.Empty<IJsonTypeInfoResolver>()
            ),
            Options.Create<IPageTokenConfiguration>(new PageTokenConfiguration { Key = key })
        );

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }

    private static ValidationException Refusal(Action decode) =>
        Assert.Throws<ValidationException>(decode);

    [Fact]
    public void ACursorRoundTrips()
    {
        var tokens = Tokens();

        Assert.Equal(Cursor, tokens.Decode<StaffCursor>(tokens.Encode(Cursor)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoTokenIsNoCursor(string? pageToken)
    {
        Assert.Null(Tokens().Decode<StaffCursor>(pageToken));
    }

    /// <summary>
    /// <c>default</c> for an absent token, so a handler that needs to tell absent from zero asks
    /// for the nullable type.
    /// </summary>
    [Fact]
    public void AValueTypeCursorRoundTrips()
    {
        var tokens = Tokens();

        Assert.Equal(42L, tokens.Decode<long>(tokens.Encode(42L)));
        Assert.Null(tokens.Decode<long?>(null));
    }

    /// <summary>
    /// The JSON <c>"a~xy?"</c> encodes to <c>+</c> and <c>/</c> in standard base64, which a query
    /// string would mangle.
    /// </summary>
    [Fact]
    public void ATokenIsWrittenInTheUrlSafeAlphabet()
    {
        var tokens = Tokens();
        var token = tokens.Encode("a~xy?");

        Assert.Contains('-', token);
        Assert.Contains('_', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.Equal("a~xy?", tokens.Decode<string>(token));
    }

    [Fact]
    public void ARefusalNamesTheArgumentTheHandlerPassed()
    {
        string? nextToken = "not a token";

        var refusal = Refusal(() => Tokens().Decode<StaffCursor>(nextToken));

        var error = Assert.Single(refusal.ValidationResult.Errors);

        Assert.Equal("nextToken", error.Field);
        Assert.Equal("invalid", error.Code);
        Assert.Equal("nextToken is not a valid page token.", error.Message);
    }

    /// <summary>
    /// Standard base64's two symbols and its padding are refused, as is a length that leaves one
    /// character over, which holds no whole byte.
    /// </summary>
    [Theory]
    [InlineData("ab+c")]
    [InlineData("ab/c")]
    [InlineData("abc=")]
    [InlineData("ab c")]
    [InlineData("a")]
    [InlineData("abcde")]
    public void ATokenOutsideTheUrlSafeAlphabetIsRefused(string pageToken)
    {
        var refusal = Refusal(() => Tokens().Decode<StaffCursor>(pageToken));

        Assert.Equal("pageToken", Assert.Single(refusal.ValidationResult.Errors).Field);
        Assert.Null(refusal.InnerException);
    }

    /// <summary>The deserializer's failure goes to the log as the inner exception.</summary>
    [Fact]
    public void ATokenThatDoesNotReadAsTheCursorIsRefused()
    {
        var tokens = Tokens();
        var pageToken = tokens.Encode("a string, not a cursor");

        var refusal = Refusal(() => tokens.Decode<StaffCursor>(pageToken));

        Assert.IsAssignableFrom<JsonException>(refusal.InnerException);
    }

    /// <summary>
    /// A JSON <c>null</c> makes <c>IJsonSerializer</c> throw a plain <c>Exception</c>, which would
    /// be a 500. <c>Encode</c> never writes one.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData(" null\n")]
    public void ATokenCarryingANullIsRefused(string json)
    {
        var pageToken = Base64Url(Encoding.UTF8.GetBytes(json));

        Refusal(() => Tokens().Decode<StaffCursor>(pageToken));
        Refusal(() => Tokens().Decode<long?>(pageToken));
    }

    [Fact]
    public void ANullCursorCannotBeEncoded()
    {
        Assert.Throws<ArgumentNullException>(() => Tokens().Encode<StaffCursor?>(null));
    }

    [Fact]
    public void ASignedCursorRoundTrips()
    {
        var tokens = Tokens("page-key");

        Assert.Equal(Cursor, tokens.Decode<StaffCursor>(tokens.Encode(Cursor)));
    }

    /// <summary>The JSON's bytes, then HMAC-SHA256 of them under the key's UTF-8 bytes.</summary>
    [Fact]
    public void ASignedTokenIsTheJsonFollowedByItsHmac()
    {
        var token = FromBase64Url(Tokens("page-key").Encode(Cursor));
        var json = FromBase64Url(Tokens().Encode(Cursor));

        Assert.Equal(json, token[..json.Length]);
        Assert.Equal(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes("page-key"), json),
            token[json.Length..]
        );
    }

    [Fact]
    public void AnEditedSignedTokenIsRefused()
    {
        var tokens = Tokens("page-key");
        var token = FromBase64Url(tokens.Encode(Cursor));
        var jsonLength = FromBase64Url(Tokens().Encode(Cursor)).Length;

        // The id's last digit, 42 to 43, which leaves the JSON valid.
        token[Array.LastIndexOf(token, (byte)'2', jsonLength - 1)] = (byte)'3';

        var refusal = Refusal(() => tokens.Decode<StaffCursor>(Base64Url(token)));

        Assert.Null(refusal.InnerException);
    }

    /// <summary>What the key is for: the same edit to an unsigned token reads as another cursor.</summary>
    [Fact]
    public void AnEditedUnsignedTokenIsAccepted()
    {
        var tokens = Tokens();
        var token = FromBase64Url(tokens.Encode(Cursor));

        token[Array.LastIndexOf(token, (byte)'2')] = (byte)'3';

        Assert.Equal(Cursor with { Id = 43 }, tokens.Decode<StaffCursor>(Base64Url(token)));
    }

    [Fact]
    public void AnUnsignedTokenIsRefusedOnceAKeyIsSet()
    {
        var pageToken = Tokens().Encode(Cursor);

        Refusal(() => Tokens("page-key").Decode<StaffCursor>(pageToken));
    }

    [Fact]
    public void ATokenSignedUnderAnotherKeyIsRefused()
    {
        var pageToken = Tokens("old-key").Encode(Cursor);

        Refusal(() => Tokens("new-key").Decode<StaffCursor>(pageToken));
    }

    [Fact]
    public void ASignedTokenShorterThanAnHmacIsRefused()
    {
        Refusal(() => Tokens("page-key").Decode<StaffCursor>("abcd"));
    }

    /// <summary>The HMAC reads as bytes after the JSON document, which the deserializer refuses.</summary>
    [Fact]
    public void ASignedTokenIsRefusedOnceTheKeyIsRemoved()
    {
        var pageToken = Tokens("page-key").Encode(Cursor);

        var refusal = Refusal(() => Tokens().Decode<StaffCursor>(pageToken));

        Assert.IsAssignableFrom<JsonException>(refusal.InnerException);
    }

    [Fact]
    public void AnEmptyKeySignsNothing()
    {
        Assert.Equal(Tokens().Encode(Cursor), Tokens("").Encode(Cursor));
    }

    /// <summary>
    /// The serializer <c>[AotSerializerModule]</c> registers reads only the registered resolvers,
    /// so a cursor declared in the application's context round-trips.
    /// </summary>
    [Fact]
    public void UnderTheAotSerializerACursorTheContextDeclaresRoundTrips()
    {
        var tokens = AotTokens(PageCursorContext.Default);
        var cursor = new ContextCursor(Cursor.CreatedAt, Cursor.Id);

        Assert.Equal(cursor, tokens.Decode<ContextCursor>(tokens.Encode(cursor)));
    }

    /// <summary>
    /// A cursor no resolver describes is the application's configuration at fault, so it stays a
    /// <c>NotSupportedException</c>, a 500, rather than becoming a 400 the client cannot fix.
    /// </summary>
    [Fact]
    public void UnderTheAotSerializerACursorNoContextDeclaresIsNotSupported()
    {
        var tokens = AotTokens();

        Assert.Throws<NotSupportedException>(() =>
            tokens.Encode(new ContextCursor(Cursor.CreatedAt, Cursor.Id))
        );
        Assert.Throws<NotSupportedException>(() =>
            tokens.Decode<ContextCursor>(Base64Url(Encoding.UTF8.GetBytes("{\"id\":1}")))
        );
    }

    private static IPageTokens AotTokens(params IJsonTypeInfoResolver[] resolvers) =>
        new PageTokens(
            new AotJsonSerializer(
                Options.Create<ISharedJsonConfiguration>(new SharedJsonConfiguration()),
                resolvers
            ),
            Options.Create<IPageTokenConfiguration>(new PageTokenConfiguration())
        );

    [Fact]
    public void TheKeyIsReadFromTheEnvironment()
    {
        var configuration = new PageTokenConfiguration();

        PageTokenConfiguration.FromEnvironment(
            new EnvironmentImpl(
                environmentValues: new Dictionary<string, string>
                {
                    [PageTokenConfiguration.EnvironmentVariable] = "from-the-environment",
                }
            ),
            configuration
        );

        Assert.Equal("HARDENED_PAGE_TOKEN_KEY", PageTokenConfiguration.EnvironmentVariable);
        Assert.Equal("from-the-environment", configuration.Key);
    }
}
