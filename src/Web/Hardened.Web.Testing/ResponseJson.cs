using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Web.Testing;

/// <summary>
/// How a test reads a JSON response: with what the application's response serializer writes with.
/// </summary>
/// <remarks>
/// <para>
/// <c>SystemTextJsonResponseSerializer</c> writes with <see cref="IJsonSerializerConfiguration.SerializeOptions"/>,
/// or System.Text.Json's web defaults where none is set, and every registered
/// <see cref="IJsonTypeInfoResolver"/> ahead of reflection. The converters the build writes for an
/// enum's vocabulary are registered that way, so a reader without the resolvers read
/// <c>"inProgress"</c> as an enum's number and threw.
/// </para>
/// <para>
/// Names still match without regard to case, as the web defaults this replaced did, so a test type
/// whose property differs from the wire name only in case keeps reading.
/// </para>
/// </remarks>
internal static class ResponseJson
{
    /// <summary>What a response read outside <see cref="ITestWebApp"/> is read with.</summary>
    public static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    public static JsonSerializerOptions OptionsFor(IServiceProvider rootServiceProvider)
    {
        var configured = rootServiceProvider
            .GetService<IOptions<IJsonSerializerConfiguration>>()
            ?.Value.SerializeOptions;

        var options = new JsonSerializerOptions(configured ?? WebDefaults)
        {
            PropertyNameCaseInsensitive = true,
        };

        return Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.WithResolvers(
            options,
            rootServiceProvider.GetServices<IJsonTypeInfoResolver>()
        );
    }
}
