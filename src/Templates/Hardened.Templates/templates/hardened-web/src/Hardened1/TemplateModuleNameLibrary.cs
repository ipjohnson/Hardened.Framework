using Hardened.Shared.Runtime.Attributes;
#if (codeFirst)
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;
#endif
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened1;

/// <summary>
/// The library module: this assembly's handlers, services and URL space.
/// </summary>
/// <remarks>
/// The host imports it with the single generated [TemplateModuleNameLibrary] attribute. partial is not
/// optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
#if (codeFirst)
// This assembly's URL space. Every route below it is relative to this.
[BasePath("/todos")]
// Embeds the document the build wrote from this assembly's routes, and serves it at
// /openapi.json.
//
// Here rather than on the host module, and that is not a style choice. The generator writes the
// document from the routes in the compilation the attribute sits in, and the host declares none -
// so on the host it emits "paths": {} and serves an empty document with no diagnostic. Worse, with
// the attribute on both, the empty one shadows this one.
//
// Spec-first applications do not use this at all: their document is a build input, published by
// PublishUrl on the spec item instead.
[Enable<OpenApiDocumentPublishing>]
#endif
public partial class TemplateModuleNameLibrary;
