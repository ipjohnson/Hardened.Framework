namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// This service answers every failed request as JSON, whatever the request negotiated.
/// </summary>
/// <remarks>
/// <para>
/// On the entry point, and one answer for the whole service - the same shape and the same place as
/// <c>[ContentNegotiation]</c> and <c>[CaseInsensitiveRoutes]</c>. See
/// <see cref="Serializer.ErrorBodyFormat"/> for what it is for and why it is not per operation.
/// </para>
/// <para>
/// <b>A marker rather than a mode.</b> There are two answers - the negotiated representation, or
/// JSON - and the first is what a service that says nothing already does, so the only thing worth
/// writing is the departure from it. Absent means negotiated.
/// </para>
/// <para>
/// A description says the same thing with <c>x-hardened-error-bodies: json</c> at its root.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [MessagePackSerializerLibrary]
/// [JsonErrorBodies]
/// public partial class MyApplication { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public class JsonErrorBodiesAttribute : Attribute;
