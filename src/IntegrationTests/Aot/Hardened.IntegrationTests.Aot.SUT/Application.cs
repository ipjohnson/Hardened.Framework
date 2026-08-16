using System.Text.Json.Serialization;
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Aot.SUT;

/// <summary>
/// The application. Identical to the Kestrel integration app but for one import.
/// </summary>
/// <remarks>
/// <c>[AotSerializerModule]</c> is what an application publishing with Native AOT adds: it puts the
/// source-generated serializers ahead of the reflection-based ones, which are annotated
/// <c>RequiresDynamicCode</c> and would otherwise be a warning here rather than a surprise after
/// publishing.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[AotSerializerModule]
public partial class Application { }

public record Echo(string Message, int Length);

/// <summary>
/// The metadata the AOT serializers resolve <see cref="Echo"/> through.
/// </summary>
/// <remarks>
/// Registered as an <c>IJsonTypeInfoResolver</c> in Program.cs. Without it the serializers throw
/// rather than silently reflecting, which is the whole difference between this and a
/// reflection-based host - and is what makes a missing context a startup-shaped problem instead of
/// a request that behaves differently once published.
/// </remarks>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Echo))]
public partial class AotContext : JsonSerializerContext { }

/// <summary>A route per thing worth proving survives ILC: routing, binding, serialization.</summary>
public class EchoController {

    [Get("/echo/{message}")]
    public Echo Get(string message) => new(message, message.Length);

    [Get("/health")]
    public string Health() => "ok";
}
