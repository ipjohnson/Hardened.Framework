using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Paging;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;
using ValidationModules;

namespace Hardened.Requests.Runtime.Paging;

/// <summary>
/// The <see cref="IPageTokens"/> every application gets.
/// </summary>
/// <remarks>
/// A signed token is the JSON's bytes followed by their HMAC, encoded as one base64url string, so
/// its shape does not say whether it is signed.
/// </remarks>
internal sealed class PageTokens : IPageTokens
{
    private readonly IJsonSerializer _serializer;
    private readonly byte[]? _key;

    public PageTokens(IJsonSerializer serializer, IOptions<IPageTokenConfiguration> configuration)
    {
        _serializer = serializer;

        var key = configuration.Value.Key;

        _key = string.IsNullOrEmpty(key) ? null : Encoding.UTF8.GetBytes(key);
    }

    public string Encode<TCursor>(TCursor cursor)
    {
        if (cursor is null)
        {
            throw new ArgumentNullException(
                nameof(cursor),
                "A token carries the cursor of the page before it, and the first page has none."
            );
        }

        var json = Encoding.UTF8.GetBytes(_serializer.Serialize(cursor));

        if (_key == null)
        {
            return ToBase64Url(json);
        }

        var token = new byte[json.Length + HMACSHA256.HashSizeInBytes];

        json.CopyTo(token, 0);
        HMACSHA256.HashData(_key, json, token.AsSpan(json.Length));

        return ToBase64Url(token);
    }

    public TCursor? Decode<TCursor>(string? pageToken, string field = "")
    {
        if (string.IsNullOrEmpty(pageToken))
        {
            return default;
        }

        var json = Read(pageToken);

        if (json == null)
        {
            throw Invalid(field, null);
        }

        try
        {
            return _serializer.Deserialize<TCursor>(json);
        }
        catch (JsonException exception)
        {
            throw Invalid(field, exception);
        }
    }

    /// <summary>The JSON a token carries, or null when this did not write it.</summary>
    private string? Read(string pageToken)
    {
        var token = FromBase64Url(pageToken);

        if (token == null)
        {
            return null;
        }

        var length = token.Length;

        if (_key != null)
        {
            length -= HMACSHA256.HashSizeInBytes;

            if (length < 0)
            {
                return null;
            }

            Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];

            HMACSHA256.HashData(_key, token.AsSpan(0, length), expected);

            if (!CryptographicOperations.FixedTimeEquals(expected, token.AsSpan(length)))
            {
                return null;
            }
        }

        var json = Encoding.UTF8.GetString(token, 0, length);

        // IJsonSerializer answers a document that reads as null with a plain Exception, which is a
        // 500. Encode never writes one, so a token carrying it was written by hand.
        return json.AsSpan().Trim() is "null" ? null : json;
    }

    private static Validation.ValidationException Invalid(string field, Exception? inner)
    {
        var result = ValidationResult.FromErrors(
            new[]
            {
                new ValidationError(
                    field,
                    ValidationCodes.Invalid,
                    $"{field} is not a valid page token."
                ),
            }
        );

        return inner == null
            ? new Validation.ValidationException(result)
            : new Validation.ValidationException(result, inner);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The bytes <paramref name="token"/> encodes, or null when it is not base64url.</summary>
    /// <remarks>
    /// Only the URL-safe alphabet with no padding, which is all <see cref="ToBase64Url"/> writes.
    /// A length that leaves one character over holds no whole byte. Past those two checks the
    /// decode cannot fail, because <see cref="Convert"/> ignores the unused bits of the last
    /// character.
    /// </remarks>
    private static byte[]? FromBase64Url(string token)
    {
        if (token.Length % 4 == 1)
        {
            return null;
        }

        var chars = new char[(token.Length + 3) / 4 * 4];

        for (var index = 0; index < token.Length; index++)
        {
            var character = token[index];

            if (character == '-')
            {
                chars[index] = '+';
            }
            else if (character == '_')
            {
                chars[index] = '/';
            }
            else if (char.IsAsciiLetterOrDigit(character))
            {
                chars[index] = character;
            }
            else
            {
                return null;
            }
        }

        chars.AsSpan(token.Length).Fill('=');

        return Convert.FromBase64CharArray(chars, 0, chars.Length);
    }
}
