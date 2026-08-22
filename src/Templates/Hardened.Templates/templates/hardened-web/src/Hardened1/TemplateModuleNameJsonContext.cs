#if (codeFirst)
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened1;

/// <summary>
/// How this application's own types reach the wire.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an <c>IJsonTypeInfoResolver</c> in <c>TemplateModuleNameLibrary</c>, which is what
/// puts it ahead of reflection for every serializer in the pipeline - the request body, the
/// response body and a streamed item all resolve through the same chain.
/// </para>
/// <para>
/// <b>Enums are the reason this is here rather than optional.</b> System.Text.Json writes an enum
/// as its number unless something says otherwise, so a <c>Priority</c> property leaves as
/// <c>{"priority":0}</c> and a client sending <c>"high"</c> is answered 400 - in an application
/// that builds clean and has no obvious defect to find. <c>UseStringEnumConverter</c> is the
/// setting that changes it, and it is set below.
/// </para>
/// <para>
/// It is set <em>here</em>, rather than by attributing the enum with
/// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>, because that converter builds a
/// converter per enum at run time and Native AOT cannot. The attribute works on a JIT host and
/// stops working when the application is published - the direction of failure that is found last.
/// A source-generated context has the enum at compile time and carries no such trap. If you do
/// reach for the attribute, use the generic <c>JsonStringEnumConverter&lt;TEnum&gt;</c>; the
/// compiler says so as SYSLIB1034 when it can see the enum from here.
/// </para>
/// <para>
/// The value written is the C# member name - <c>{"priority":"High"}</c>, a PascalCase value in a
/// camelCase property, because <c>PropertyNamingPolicy</c> governs property names and does not
/// reach enum members. An application that wants <c>"high"</c> or <c>"in-progress"</c> on the wire
/// says so with a converter carrying that vocabulary:
/// <code>
/// public sealed class KebabEnum&lt;T&gt; : JsonStringEnumConverter&lt;T&gt;
///     where T : struct, Enum {
///     public KebabEnum() : base(JsonNamingPolicy.KebabCaseLower) { }
/// }
/// </code>
/// That is a wire-format decision, so make it before the first client rather than after.
/// </para>
/// <para>
/// Every type serialized by name needs a <c>[JsonSerializable]</c> line. A type with none is still
/// served by reflection on a JIT host, and is the exception a published AOT build throws.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(NewTodo))]
public partial class TemplateModuleNameJsonContext : JsonSerializerContext;
#endif
