using System;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl.Validation;
using Hardened.Idl.Emitters;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Runs one emitter on its own and returns what it wrote.
/// </summary>
/// <remarks>
/// The emitters add types to a container rather than returning a file, because a build task composes
/// one file per spec from all of them. Testing one in isolation therefore needs a container to add
/// to and an <see cref="OutputContext"/> to write through - which is what this is. Whole-file
/// composition, including which namespaces exist and which types carry
/// <c>[ExcludeFromCodeCoverage]</c>, is <see cref="SpecFileEmitter"/>'s and is tested there.
/// </remarks>
internal static class EmitterHarness {

    internal const string RootNamespace = "Test.Api";

    internal const string ModelsNamespace = RootNamespace + ".Models";

    /// <summary>Emits into a namespace block and returns the file text, usings and all.</summary>
    internal static string Write(Action<NamespaceDefinition> emit, string ns = ModelsNamespace) {
        var file = new CSharpFileDefinition();
        var namespaceDefinition = new NamespaceDefinition(ns);

        file.AddComponent(namespaceDefinition);

        emit(namespaceDefinition);

        var context = new OutputContext();

        file.WriteOutput(context);

        return context.Output();
    }

    internal static string Schema(SchemaModel schema) =>
        Write(ns => SchemaEmitter.Emit(
            ns, schema, ModelsNamespace, new PatternRegistry(RootNamespace + ".Validation", "petstore")));

    /// <summary>
    /// The same, with the schemas a property's <c>$ref</c> can resolve against.
    /// </summary>
    /// <remarks>
    /// Needed to emit <c>[ValidateNested]</c> at all: whether a property is descended into depends
    /// on whether the schema behind its ref will get a validator, and that cannot be answered from
    /// the property alone.
    /// </remarks>
    internal static string Schema(
        SchemaModel schema, System.Collections.Generic.List<SchemaModel> allSchemas) =>
        Write(ns => SchemaEmitter.Emit(
            ns, schema, ModelsNamespace,
            new PatternRegistry(RootNamespace + ".Validation", "petstore"), allSchemas));

    internal static string ServiceInterface(ServiceModel service) =>
        Write(ns => ServiceInterfaceEmitter.Emit(ns, service, ModelsNamespace),
            RootNamespace + ".Services");

    internal static string JsonTypeInfo(
        System.Collections.Generic.List<SchemaModel> schemas, string specFileName) =>
        Write(ns => JsonTypeInfoEmitter.Emit(ns, schemas, ModelsNamespace, specFileName));
}
