using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// The C# for one application's functions, through CSharpAuthor.
/// </summary>
/// <remarks>
/// <para>
/// Two files, for two audiences. The first is what the worker sees: the shims, the metadata
/// provider and the executor, in the application's <c>Generated</c> namespace beside the invokers
/// the function generator writes. The second is a partial of the entry point that registers the
/// provider and the executor, which is the one piece the runtime package cannot write because it
/// cannot name generated types.
/// </para>
/// <para>
/// The bodies that are one statement of worker API are written as text, fully qualified, the way
/// <c>TriggerModuleGenerator</c> writes its registration: their shape is fixed and assembling them
/// from type definitions would say less.
/// </para>
/// </remarks>
internal static class AzureFunctionsEmitter {

    private const string Worker = "Microsoft.Azure.Functions.Worker";

    private static readonly ITypeDefinition FunctionAttribute =
        TypeDefinition.Get(Worker, "FunctionAttribute");

    private static readonly ITypeDefinition FunctionContext =
        TypeDefinition.Get(Worker, "FunctionContext");

    private static readonly ITypeDefinition FunctionMetadata =
        TypeDefinition.Get(Worker + ".Core.FunctionMetadata", "IFunctionMetadata");

    private static readonly ITypeDefinition FunctionMetadataProvider =
        TypeDefinition.Get(Worker + ".Core.FunctionMetadata", "IFunctionMetadataProvider");

    private static readonly ITypeDefinition FunctionExecutor =
        TypeDefinition.Get(Worker + ".Invocation", "IFunctionExecutor");

    private static readonly ITypeDefinition Task =
        TypeDefinition.Get("System.Threading.Tasks", "Task");

    private static readonly ITypeDefinition ValueTask =
        TypeDefinition.Get("System.Threading.Tasks", "ValueTask");

    private static readonly ITypeDefinition ServiceCollection =
        TypeDefinition.Get("Microsoft.Extensions.DependencyInjection", "IServiceCollection");

    private static readonly ITypeDefinition HardenedFunctionsApplication =
        TypeDefinition.Get("Hardened.Azure.Functions.Runtime.Hosting", "IHardenedFunctionsApplication");

    private const string InvocationHandler = "global::Hardened.Azure.Functions.Runtime.Hosting.FunctionsInvocationHandler";

    private const string DispatchEnum = "global::Hardened.Azure.Functions.Runtime.Execution.FunctionsDispatch";

    public sealed class Emitted {
        public Emitted(string functions, string worker) {
            Functions = functions;
            Worker = worker;
        }

        /// <summary>The shims, the provider and the executor.</summary>
        public string Functions { get; }

        /// <summary>The entry point's registration of the provider and the executor.</summary>
        public string Worker { get; }
    }

    public static Emitted Emit(
        EntryPointSelector.Model entryPoint,
        IReadOnlyList<AzureFunctionsGenerator.Function> functions,
        string assemblyName) {
        var ns = entryPoint.EntryPointType.Namespace;
        var app = entryPoint.EntryPointType.Name;

        var shims = TypeDefinition.Get(ns + ".Generated", app + "AzureFunctions");
        var provider = TypeDefinition.Get(ns + ".Generated", app + "AzureFunctionMetadataProvider");
        var executor = TypeDefinition.Get(ns + ".Generated", app + "AzureFunctionExecutor");

        var file = new CSharpFileDefinition(ns + ".Generated");

        WriteShims(file.AddClass(shims.Name), functions);
        WriteProvider(file.AddClass(provider.Name), shims, functions, assemblyName);
        WriteExecutor(file.AddClass(executor.Name), shims, functions);

        var worker = new CSharpFileDefinition(ns);

        // AddSingleton is an extension method, and an extension method is reachable only through
        // a using of its namespace - global:: cannot name one.
        worker.AddUsingNamespace("Microsoft.Extensions.DependencyInjection");

        WriteRegistration(worker.AddClass(app), provider, executor);

        return new Emitted(Output(file), Output(worker));
    }

    /// <summary>
    /// The functions themselves: one static method per handler or family, carrying the worker's
    /// binding attribute and handing the invocation handler the route it was generated for.
    /// </summary>
    /// <remarks>
    /// Static, and public in a public class, because two readers locate it by name: the Worker
    /// SDK's build task scans the compiled assembly for it, and the worker resolves the entry
    /// point it was told through reflection to learn the parameters. The generated executor calls
    /// it directly, so the reflection never runs an invocation.
    /// </remarks>
    private static void WriteShims(
        ClassDefinition shims, IReadOnlyList<AzureFunctionsGenerator.Function> functions) {
        shims.Modifiers = ComponentModifier.Public | ComponentModifier.Static;

        foreach (var function in functions) {
            var binding = function.Binding;
            var arguments = binding.Arguments(function.Source, function.Settings);

            var method = shims.AddMethod(function.Name);

            method.Modifiers = ComponentModifier.Public | ComponentModifier.Static;
            method.AddAttribute(FunctionAttribute, QuoteString(function.Name));

            foreach (var parameter in binding.Parameters) {
                var declared = method.AddParameter(parameter.Type, parameter.Name);

                if (!parameter.IsTrigger) {
                    continue;
                }

                var attribute = declared.AddAttribute(binding.Attribute, arguments.Positional.Cast<object>().ToArray());

                foreach (var named in arguments.Named) {
                    attribute.AddNamedArgument(named.Key, new CodeOutputComponent(named.Value) { Indented = false });
                }
            }

            method.AddParameter(FunctionContext, "context");

            var invoke = InvocationHandler + ".Invoke(context, " + QuoteString(function.Scheme) + ", " +
                         QuoteString(function.Path) + ", " + binding.DataExpression + ", " +
                         DispatchEnum + "." + binding.Dispatch + ")";

            if (binding.ReturnType == null) {
                method.SetReturnType(Task);
                method.Return(new CodeOutputComponent(invoke) { Indented = false });
            }
            else {
                // The one family whose function answers the host. The invocation handler returns
                // what the adapter built as an object, and the shim, which knows the family, casts.
                method.Modifiers |= ComponentModifier.Async;
                method.SetReturnType(new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition, "System.Threading.Tasks", "Task", new[] { binding.ReturnType }));
                method.Return(new CodeOutputComponent(
                    "(" + Name(binding.ReturnType) + ")(await " + invoke + ")!") { Indented = false });
            }
        }
    }

    /// <summary>
    /// What the host indexes: the same functions, described the way the Worker SDK's build task
    /// describes them in <c>functions.metadata</c>.
    /// </summary>
    private static void WriteProvider(
        ClassDefinition provider,
        ITypeDefinition shims,
        IReadOnlyList<AzureFunctionsGenerator.Function> functions,
        string assemblyName) {
        provider.Modifiers = ComponentModifier.Public | ComponentModifier.Sealed;
        provider.AddBaseType(FunctionMetadataProvider);

        var method = provider.AddMethod("GetFunctionMetadataAsync");

        method.SetReturnType(new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System.Threading.Tasks", "Task",
            new ITypeDefinition[] {
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition, "System.Collections.Immutable", "ImmutableArray",
                    new[] { FunctionMetadata })
            }));

        method.AddParameter(typeof(string), "directory");

        method.AddIndentedStatement(new CodeOutputComponent(
            "var functions = global::System.Collections.Immutable.ImmutableArray.CreateBuilder<global::" +
            FunctionMetadata.Namespace + "." + FunctionMetadata.Name + ">(" + functions.Count + ")") {
            Indented = false
        });

        foreach (var function in functions) {
            var bindings = string.Join(
                ", ",
                function.Binding.RawBindings(function.Source, function.Settings).Select(QuoteString));

            method.AddIndentedStatement(new CodeOutputComponent(
                "functions.Add(new global::" + Worker + ".Core.FunctionMetadata.DefaultFunctionMetadata { " +
                "Language = \"dotnet-isolated\", " +
                "Name = " + QuoteString(function.Name) + ", " +
                "EntryPoint = " + QuoteString(shims.Namespace + "." + shims.Name + "." + function.Name) + ", " +
                "ScriptFile = " + QuoteString(assemblyName + ".dll") + ", " +
                "RawBindings = new global::System.Collections.Generic.List<string> { " + bindings + " } })") {
                Indented = false
            });
        }

        method.Return(new CodeOutputComponent(
            "global::System.Threading.Tasks.Task.FromResult(functions.ToImmutable())") { Indented = false });
    }

    /// <summary>
    /// What the worker runs: a switch on the function's name to the shim, with every parameter
    /// bound through the worker's own input binding feature, which is the path the Worker SDK's
    /// executor takes and what runs the extensions' converters.
    /// </summary>
    /// <remarks>
    /// The name rather than the entry point, which the Worker SDK's executor switches on: both are
    /// this generator's own strings, and the name is the one a log line shows. A function the
    /// switch does not know is an error by name, because the host only ever invokes what the
    /// provider listed.
    /// </remarks>
    private static void WriteExecutor(
        ClassDefinition executor,
        ITypeDefinition shims,
        IReadOnlyList<AzureFunctionsGenerator.Function> functions) {
        executor.Modifiers = ComponentModifier.Public | ComponentModifier.Sealed;
        executor.AddBaseType(FunctionExecutor);

        var method = executor.AddMethod("ExecuteAsync");

        method.Modifiers = ComponentModifier.Public | ComponentModifier.Async;
        method.SetReturnType(ValueTask);
        method.AddParameter(FunctionContext, "context");

        // No switch at all for no functions: an empty switch block is CS1522, a warning that is an
        // error wherever warnings are.
        if (functions.Count > 0) {
            var switchBlock = method.Switch(
                new CodeOutputComponent("context.FunctionDefinition.Name") { Indented = false });

            foreach (var function in functions) {
                var binding = function.Binding;
                var caseBlock = switchBlock.AddCase(QuoteString(function.Name));

                // Named after the function, because every section of a switch shares one scope.
                var inputs = function.Name + "_inputs";

                caseBlock.AddIndentedStatement(new CodeOutputComponent(
                    "var " + inputs + " = (await context.Features.Get<global::" + Worker +
                    ".Context.Features.IFunctionInputBindingFeature>()!.BindFunctionInputAsync(context)).Values") {
                    Indented = false
                });

                var arguments = new List<string>();

                for (var index = 0; index < binding.Parameters.Count; index++) {
                    arguments.Add("(" + Name(binding.Parameters[index].Type) + ")" + inputs + "[" + index + "]!");
                }

                arguments.Add("(" + Name(FunctionContext) + ")" + inputs + "[" + binding.Parameters.Count + "]!");

                var call = "global::" + shims.Namespace + "." + shims.Name + "." + function.Name + "(" +
                           string.Join(", ", arguments) + ")";

                caseBlock.AddIndentedStatement(new CodeOutputComponent(
                    binding.ReturnType == null
                        ? "await " + call
                        : "global::" + Worker + ".FunctionContextBindingFeatureExtensions.GetInvocationResult(context).Value = await " + call) {
                    Indented = false
                });

                caseBlock.Return();
            }
        }

        method.AddIndentedStatement(new CodeOutputComponent(
            "throw new global::System.InvalidOperationException(" +
            QuoteString("No Hardened function is compiled for '") +
            " + context.FunctionDefinition.Name + " +
            QuoteString("'. The host invoked a function the generated metadata provider did not list, " +
                        "so the two were built from different code.") + ")") {
            Indented = false
        });
    }

    /// <summary>
    /// The registration <c>UseHardened</c> calls, on a partial of the entry point.
    /// </summary>
    private static void WriteRegistration(
        ClassDefinition app, ITypeDefinition provider, ITypeDefinition executor) {
        app.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;
        app.AddBaseType(HardenedFunctionsApplication);

        var method = app.AddMethod("ConfigureFunctionsWorker");

        var services = method.AddParameter(ServiceCollection, "services");

        method.AddIndentedStatement(
            services.InvokeGeneric("AddSingleton", new[] { FunctionMetadataProvider, provider }));

        method.AddIndentedStatement(
            services.InvokeGeneric("AddSingleton", new[] { FunctionExecutor, executor }));
    }

    /// <summary>A type as generated text: fully qualified, with its array rank.</summary>
    private static string Name(ITypeDefinition type) =>
        "global::" + type.Namespace + "." + type.Name + (type.IsArray ? "[]" : "");

    private static string Output(CSharpFileDefinition file) {
        var output = new OutputContext(new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global });

        file.WriteOutput(output);

        return output.Output();
    }
}
