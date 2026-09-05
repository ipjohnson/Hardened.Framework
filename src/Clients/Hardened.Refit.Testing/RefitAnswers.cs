using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Reflection;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Testing;
using Refit;

namespace Hardened.Refit.Testing;

/// <summary>
/// What a call through a Refit client answered, from the two things one produces.
/// </summary>
/// <remarks>
/// <para>
/// Refit hands the status, the headers and the body over on one object. A method declared
/// <c>Task&lt;IApiResponse&lt;T&gt;&gt;</c> - Refitter's <c>--use-api-response</c> - returns the
/// envelope for every status and throws for none. A method declared <c>Task&lt;T&gt;</c> throws an
/// <see cref="ApiException"/> carrying the same three for a refusal, which is read the same way,
/// and returns the body alone for a success - where the status and the headers are gone, so that
/// shape is not recognised and <c>Returns</c> refuses it by name rather than guessing a 200.
/// </para>
/// <para>
/// Refit has no error mapping, so an error body arrives as text. It is read through the
/// exception's own <see cref="ApiException.GetContentAsAsync{T}"/>, which goes through the client's
/// <see cref="RefitSettings"/>: the assertion sees what a consumer of the client would, and a body
/// the client's serializer cannot read fails here as it would there.
/// </para>
/// </remarks>
internal static class RefitAnswers {

    private static readonly MethodInfo Reader =
        typeof(RefitAnswers).GetMethod(nameof(ReadAs), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, MethodInfo> Readers = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> Contents = new();

    public static async Task<ClientAnswer?> Read(object? result, Exception? thrown, Type? bodyType) {
        if (thrown is ApiException refusal) {
            return new ClientAnswer(
                (int)refusal.StatusCode,
                await ErrorBody(refusal, bodyType),
                Flatten(refusal.Headers, refusal.ContentHeaders));
        }

        if (thrown == null && result is IApiResponse response) {
            var body = response.Error is { } error
                ? await ErrorBody(error, bodyType)
                : Content(response);

            return new ClientAnswer(
                (int)response.StatusCode, body, Flatten(response.Headers, response.ContentHeaders));
        }

        return null;
    }

    /// <summary>
    /// The error body as the expectation's type, through the client's own serializer. The text as
    /// it arrived where there is no type to read it as, so a status mismatch can still say what
    /// came back.
    /// </summary>
    private static async Task<object?> ErrorBody(ApiException error, Type? bodyType) {
        if (!error.HasContent) {
            return null;
        }

        if (bodyType == null) {
            return error.Content;
        }

        var reader = Readers.GetOrAdd(bodyType, type => Reader.MakeGenericMethod(type));

        try {
            return await (Task<object?>)reader.Invoke(null, [error])!;
        } catch (Exception failure) {
            throw new InvalidOperationException(
                $"The {(int)error.StatusCode} body could not be read as " +
                $"{ResponseExpectation.Name(bodyType)} through the client's serializer: " +
                failure.Message, failure);
        }
    }

    private static async Task<object?> ReadAs<T>(ApiException error) => await error.GetContentAsAsync<T>();

    /// <summary>The envelope's content for an <see cref="IApiResponse{T}"/>; null for the bare envelope.</summary>
    private static object? Content(IApiResponse response) =>
        Contents.GetOrAdd(response.GetType(), type => type
                .GetInterfaces()
                .FirstOrDefault(contract =>
                    contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IApiResponse<>))
                ?.GetProperty(nameof(IApiResponse<object>.Content)))
            ?.GetValue(response);

    /// <summary>
    /// Response headers and content headers together, because both are headers on the response and
    /// only the transport draws the line between them.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Flatten(HttpHeaders headers, HttpHeaders? contentHeaders) {
        var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers) {
            flattened[header.Key] = string.Join(", ", header.Value);
        }

        if (contentHeaders != null) {
            foreach (var header in contentHeaders) {
                flattened[header.Key] = string.Join(", ", header.Value);
            }
        }

        return flattened;
    }
}
