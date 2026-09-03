using Hardened.Generation;
using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.Idl.Validation;
using Hardened.Generation.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Composes one spec's generated C# into a single file.
/// </summary>
/// <remarks>
/// <para>
/// This owns everything about the file: which namespaces exist, what goes in them, and which types
/// carry <c>[ExcludeFromCodeCoverage]</c>. The individual emitters own one type each and know none
/// of it - they are handed a container and add to it.
/// </para>
/// <para>
/// The emitters used to return whole files, because a source generator addresses its output by hint
/// name and every call produced one. A build task writes files itself, so that shape bought nothing
/// and cost a duplicated header, a hand-written <c>using</c> block and a namespace line in each of
/// them.
/// </para>
/// </remarks>
internal static class SpecFileEmitter {

    /// <summary>Types under the root namespace that models live in.</summary>
    private const string ModelsNamespace = "Models";

    /// <summary>Types under the root namespace that service interfaces live in.</summary>
    private const string ServicesNamespace = "Services";

    /// <summary>Types under the root namespace that parameter interfaces and patterns live in.</summary>
    private const string ValidationNamespace = "Validation";

    public static string Emit(
        ServiceSpecModel model,
        string rootNamespace,
        bool excludeFromCoverage,
        string document = "",
        string specPath = "",
        SpecResponseModel responseModel = SpecResponseModel.Throws) {
        // An unnamed namespace writes no wrapper of its own, so this is the file rather than a
        // namespace in it. Filter types declare their own namespace in the spec and may sit outside
        // the root entirely, which is why the file needs to hold more than one top-level block.
        var file = new CSharpFileDefinition();

        var root = new NamespaceDefinition(rootNamespace);
        file.AddComponent(root);

        var models = root.AddNamespace(ModelsNamespace);
        var modelsNamespace = rootNamespace + "." + ModelsNamespace;

        // Built before the schemas, because emitting a record's constraints registers any pattern
        // it uses and the [GeneratedRegex] members are written from what was registered.
        var patterns = new PatternRegistry(rootNamespace + "." + ValidationNamespace, model.FileName);

        foreach (var schema in model.Schemas) {
            Coverage.Apply(
                SchemaEmitter.Emit(models, schema, modelsNamespace, patterns, model.Schemas),
                excludeFromCoverage);
        }

        if (model.Services.Count > 0) {
            var services = root.AddNamespace(ServicesNamespace);

            var responses = new List<ClassDefinition>();

            foreach (var service in model.Services) {
                Coverage.Apply(
                    ServiceInterfaceEmitter.Emit(services, service, modelsNamespace, responseModel),
                    excludeFromCoverage);

                // In the models namespace, beside the payloads they carry, rather than beside the
                // interfaces - neither is part of the contract an implementation implements.
                //
                // The containers and their success cases are per operation, so they are emitted
                // inside this loop.
                //
                // "Never both" is an invariant about a single declared status, and it still holds:
                // an operation is wholly in one form or the other, so no 404 is ever reachable as a
                // thrown exception and as a returned case at once. What changed is that the choice
                // is no longer the module's alone - an operation declaring two successes is in the
                // response-set form whatever the module asked for, because a throw cannot carry a
                // success. A service can therefore hold some of each, and emitting only one kind
                // would leave the other operations' signatures naming types nothing wrote.
                responses.AddRange(
                    UnionResponseEmitter.Emit(
                        models, service, modelsNamespace,
                        asLanguageUnion: responseModel == SpecResponseModel.Union,
                        responseModel: responseModel));
            }

            // The types a declared error needs, once each for the whole document rather than once
            // per operation that declares it. Two operations declaring one 404 over one schema
            // used to get two classes - GetPetNotFoundException beside
            // GetPetLabelNotFoundException, the same class under two names - and two services
            // declaring it would have emitted the same class twice into one namespace.
            //
            // Partitioned by response model, because a declared status has exactly one way to be
            // answered: the operations that throw need an exception, the ones returning a set need
            // a case type, and an error declared by some of each needs both.
            var thrown = GeneratedErrors(model, responseModel, inResponseSet: false);

            responses.AddRange(ErrorResponseEmitter.Emit(models, thrown, modelsNamespace));

            // Throwing shorthand for those, so the exception is inferred from the body rather than
            // named beside it. Only for the ones this file wrote a type for: an error that binds to
            // a shipped record reaches AsException() through the generic extension already.
            var factories = ErrorFactoryEmitter.Emit(
                models, thrown, modelsNamespace, model.FileName);

            if (factories != null) {
                responses.Add(factories);
            }

            responses.AddRange(
                UnionResponseEmitter.EmitErrorCaseTypes(
                    models, GeneratedErrors(model, responseModel, inResponseSet: true),
                    modelsNamespace));

            foreach (var definition in responses) {
                Coverage.Apply(definition, excludeFromCoverage);
            }

            // The body a null return writes, one instance per (schema, status) an operation could
            // answer with. Beside the exceptions for the same reason: it is a payload, not part of
            // the contract the implementation implements.
            DefaultErrorBodyEmitter.Emit(
                models, model.Schemas, NullResponseBodies(model), modelsNamespace);
        }

        Coverage.Apply(
            JsonTypeInfoEmitter.Emit(models, model.Schemas, modelsNamespace, model.FileName),
            excludeFromCoverage);

        // The specification itself, so the application can serve the contract it was built from
        // rather than a second description of it. Under the root namespace rather than Models,
        // because it is not one - it is the input.
        if (document.Length > 0) {
            Coverage.Apply(
                SpecificationDocumentEmitter.Emit(root, model, document, specPath),
                excludeFromCoverage);
        }

        EmitFilterTypes(file, model, excludeFromCoverage);

        // Interfaces first: building them registers the patterns their constraints reference, and
        // the [GeneratedRegex] members are written from the registry once it is complete.
        var validation = root.AddNamespace(ValidationNamespace);
        var operations = ValidationEmitter.Emit(validation, model, modelsNamespace, patterns);

        // Before the patterns are written, because assigning route constraints registers the
        // patterns they compile to and EmitPatterns writes from what was registered.
        RouteConstraintEmitter.Emit(validation, model, patterns);

        ValidationEmitter.EmitPatterns(validation, patterns);

        // Recorded so the generator is told which interface each handler implements, rather than
        // deriving the name a second time and drifting.
        model.ValidatedOperations = operations
            .Select(operation => new ValidatedOperationModel {
                OperationId = operation.OperationId,
                InterfaceName = operation.InterfaceName,
            })
            .ToList();

        // Fully qualified, because the names in this file come from someone else's document and
        // routinely collide with the BCL: GitHub declares Environment and Thread, Stripe declares
        // File, and each of those was CS0104 against System.Environment, System.Threading.Thread
        // and System.IO.File. Nobody reads this file, so the length costs nothing, and it also
        // means a type the consumer declares can never change what generated code binds to.
        var context = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        file.WriteOutput(context);

        return "// <auto-generated/>\n#nullable enable\n\n" + context.Output();
    }

    /// <summary>
    /// Every declared error that needs a type generated for it, once each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinct-set pass the error emitters take their input from, in the shape
    /// <see cref="DefaultErrorBodyEmitter"/> already used: the set is computed here, over the whole
    /// document, and the emitter writes what it is given. Keyed by
    /// <c>ShippedResponses.GeneratedKey</c> rather than by name, because two errors wanting one
    /// name over different payloads are two types and collapsing them by name would emit one record
    /// and reference it for both.
    /// </para>
    /// <para>
    /// Most declared errors are in neither set. <c>ShippedResponses.For</c> binds them to a record
    /// the framework already ships, and <c>NameAllocator</c> left their <c>TypeName</c> null for
    /// exactly that reason.
    /// </para>
    /// </remarks>
    /// <param name="inResponseSet">
    /// Whether to collect the errors of the operations that answer with a response set, or of the
    /// ones that throw. An error declared by both kinds appears in both, and gets a case type and
    /// an exception - separately named, so neither collides with the other.
    /// </param>
    private static IReadOnlyList<ErrorResponseModel> GeneratedErrors(
        ServiceSpecModel model, SpecResponseModel responseModel, bool inResponseSet) {
        var collected = new List<ErrorResponseModel>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                if (ResponseSetPlan.RequiresResponseSet(operation, responseModel) != inResponseSet) {
                    continue;
                }

                foreach (var error in operation.ErrorResponses) {
                    if (ShippedResponses.For(error) != null) {
                        continue;
                    }

                    if (seen.Add(ShippedResponses.GeneratedKey(error))) {
                        collected.Add(error);
                    }
                }
            }
        }

        return collected;
    }

    /// <summary>
    /// Every (schema, status) an operation's null return could answer with.
    /// </summary>
    /// <remarks>
    /// A null return is 404 for GET and PUT and a success for everything else, so only those two
    /// verbs can produce an error body this way. Restricted to statuses the operation itself
    /// declares - a document that never mentions 404 for an operation is not given one here.
    /// </remarks>
    private static IReadOnlyCollection<(string SchemaName, int StatusCode)> NullResponseBodies(
        ServiceSpecModel model) {
        var wanted = new HashSet<(string, int)>();

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                if (operation.HttpMethod != "GET" && operation.HttpMethod != "PUT") {
                    continue;
                }

                foreach (var error in operation.ErrorResponses) {
                    if (error.StatusCode != 404 || error.Ref == null) {
                        continue;
                    }

                    wanted.Add((TypeMapper.GetRefName(error.Ref), error.StatusCode));
                }
            }
        }

        return wanted;
    }

    /// <summary>
    /// Filter attributes, each in the namespace its spec declared.
    /// </summary>
    /// <remarks>
    /// Those namespaces are whatever the author wrote in <c>x-filter-types</c> and need not sit under
    /// the root, so each becomes a top-level block rather than being nested. Two filter types
    /// sharing a namespace share the block - which is what
    /// <see cref="NamespaceDefinition.AddNamespace"/> does within a namespace, and what this does at
    /// file level, where there is nothing to ask.
    /// </remarks>
    private static void EmitFilterTypes(
        CSharpFileDefinition file, ServiceSpecModel model, bool excludeFromCoverage) {
        var namespaces = new Dictionary<string, NamespaceDefinition>(System.StringComparer.Ordinal);

        foreach (var filterType in model.FilterTypes) {
            if (!filterType.Generate) {
                continue;
            }

            if (!namespaces.TryGetValue(filterType.Namespace, out var filterNamespace)) {
                filterNamespace = new NamespaceDefinition(filterType.Namespace);
                namespaces.Add(filterType.Namespace, filterNamespace);
                file.AddComponent(filterNamespace);
            }

            Coverage.Apply(FilterTypeEmitter.Emit(filterNamespace, filterType), excludeFromCoverage);
        }
    }
}
