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
/// A model listed here is one a published Native AOT build can serialize. A type with no
/// <c>[JsonSerializable]</c> line still works on a JIT host, by reflection, and is the
/// <c>NotSupportedException</c> that build throws - so the list is worth keeping complete even
/// while nothing here is published AOT.
/// </para>
/// <para>
/// <b>Enums are not configured here.</b> The build writes a converter for every enum this
/// application puts on the wire, carrying the values the generated OpenAPI document declares, and
/// registers it alongside this. <c>[JsonEnumNaming]</c> chooses the vocabulary - see
/// <c>AGENTS.md</c>. <c>UseStringEnumConverter</c> is deliberately absent: it writes the C# member
/// name, which is an identifier rather than a wire value, and it would not reach the document at
/// all.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(NewTodo))]
// List<Todo> rather than the IReadOnlyList<Todo> the handler declares. The response value reaches
// the serializer as object, so System.Text.Json resolves the runtime type - and it is the runtime
// type that needs a JsonTypeInfo here.
[JsonSerializable(typeof(List<Todo>))]
public partial class TemplateModuleNameJsonContext : JsonSerializerContext;
