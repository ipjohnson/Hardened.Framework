using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl;

[TransientService]
public class NewtonsoftDeserializer : IRequestDeserializer {
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly ISharedSerializer _sharedSerializer;
    private readonly ILogger<NewtonsoftDeserializer> _logger;

    public NewtonsoftDeserializer(IMemoryStreamPool memoryStreamPool, ISharedSerializer sharedSerializer,
        ILogger<NewtonsoftDeserializer> logger) {
        _memoryStreamPool = memoryStreamPool;
        _sharedSerializer = sharedSerializer;
        _logger = logger;
    }

    public bool IsDefaultSerializer => true;

    public bool CanProcessContext(IExecutionContext context) {
        return context.Request.ContentType?.Contains("application/json") ?? false;
    }

    /// <summary>
    /// Reads the request body and deserializes it, leaving the body open for whoever owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method returned <c>default(T)</c> for every request until 2026-08-18, and had done since
    /// the package shipped. It took one reservation from the pool, copied the body into a
    /// <em>second</em> one, seeked a <em>third</em>, and then read the first — which was empty. Two
    /// of the three were never disposed, so every request leaked a pooled stream. Even with the
    /// right stream it could not have worked: <c>ReadToEndAsync</c> ran the <c>StreamReader</c> to
    /// EOF before <c>Deserialize</c> was given the <c>JsonTextReader</c> over it, so the parser saw
    /// no tokens.
    /// </para>
    /// <para>
    /// Nothing caught it because nothing ran it. The assembly was at 0% line coverage with no test
    /// project, and no integration fixture imports the package.
    /// </para>
    /// <para>
    /// The three <c>LogInformation</c> calls went with it. They were debug leftovers, and one of
    /// them wrote the entire request body to the log at Information — every credential, token and
    /// personal detail any consumer ever posted.
    /// </para>
    /// <para>
    /// <c>leaveOpen: true</c> on the reader is load-bearing rather than tidy: <c>MemoryStreamPool</c>
    /// resets <c>Position</c> when a reservation is returned, which throws on a stream a reader
    /// closed on its way out. That is the same defect <c>JsonSerializerImpl.DeserializeAsync</c>
    /// carried until 2026-08-12.
    /// </para>
    /// </remarks>
    public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        try {
            using var buffer = _memoryStreamPool.Get();

            await context.Request.Body.CopyToAsync(buffer.Item);

            buffer.Item.Position = 0;

            using var textReader = new StreamReader(buffer.Item, null, true, -1, true);
            using var jsonReader = new JsonTextReader(textReader);

            return _sharedSerializer.Serializer.Deserialize<T>(jsonReader);
        }
        catch (Exception exp) {
            _logger.LogError(exp, "Newtonsoft deserializer threw {Message}", exp.Message);

            throw;
        }
    }
}
