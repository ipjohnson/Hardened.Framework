using Hardened.Shared.Runtime.Attributes;
#if (codeFirst)
using System.Text.Json.Serialization.Metadata;
using DependencyModules.Runtime.Interfaces;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
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
public partial class TemplateModuleNameLibrary : IServiceCollectionConfiguration {

    /// <summary>
    /// Says how this application's types are written, ahead of reflection.
    /// </summary>
    /// <remarks>
    /// One registration, read by every JSON serializer in the pipeline - the request body, the
    /// response body, and a streamed item. Enums are not configured here: the build writes a
    /// converter for each one and registers it alongside this - see
    /// <see cref="TemplateModuleNameJsonContext"/> and [JsonEnumNaming].
    ///
    /// A resolver the container does not know about is a resolver the serializers never ask, and
    /// nothing reports that - so this line is the difference between a configured context and a
    /// decorative one.
    /// </remarks>
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IJsonTypeInfoResolver>(TemplateModuleNameJsonContext.Default);
    }
}
#else
public partial class TemplateModuleNameLibrary;
#endif
