using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;

namespace Hardened.Web.SourceGenerator;

[Generator]
public class WebLibrarySourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Says "a routing generator is running" to the library generator, which reports its
        // absence - a module with handlers and no routing generator builds clean into an
        // application that answers 404 to everything. Post-init because that is the only generated
        // source another generator can see.
        context.RegisterPostInitializationOutput(static production => production.AddSource(
            "Hardened.Web.Marker.g.cs",
            GeneratedSource.Header(RoutingGeneratorMarker.Source)));

        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        WebIncrementalGenerator.Setup(context, applicationModel);
    }
}