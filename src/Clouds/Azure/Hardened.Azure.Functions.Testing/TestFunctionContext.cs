using System.Collections;
using System.Collections.Immutable;
using Microsoft.Azure.Functions.Worker;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// Enough <see cref="FunctionContext"/> to invoke a function outside the worker.
/// </summary>
/// <remarks>
/// <para>
/// The worker's context is an abstract class the gRPC layer implements from the host's invocation
/// request, and nothing public builds one. This is the same shape with the pieces a test can
/// supply: the function's name, the binding data the host would send, the services to resolve
/// from, and a cancellation token the test controls.
/// </para>
/// <para>
/// The definition carries no parameters and no bindings. Those are what the worker's own executor
/// reads to bind inputs, and this context never reaches an executor - the delivery hands the
/// invocation handler the data directly, which is what the generated shim does after the worker
/// has bound it.
/// </para>
/// </remarks>
public sealed class TestFunctionContext : FunctionContext {
    public TestFunctionContext(
        string functionName,
        IReadOnlyDictionary<string, object?> bindingData,
        IServiceProvider services,
        CancellationToken cancellationToken = default) {
        FunctionDefinition = new Definition(functionName);
        BindingContext = new Bindings(bindingData);
        InstanceServices = services;
        CancellationToken = cancellationToken;
    }

    public override string InvocationId { get; } = Guid.NewGuid().ToString();

    public override string FunctionId => FunctionDefinition.Id;

    public override TraceContext TraceContext { get; } = new Trace();

    public override BindingContext BindingContext { get; }

    public override RetryContext RetryContext { get; } = new Retry();

    public override IServiceProvider InstanceServices { get; set; }

    public override FunctionDefinition FunctionDefinition { get; }

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

    public override IInvocationFeatures Features { get; } = new FeatureCollection();

    public override CancellationToken CancellationToken { get; }

    private sealed class Definition : FunctionDefinition {
        public Definition(string name) {
            Name = name;
            EntryPoint = name;
            Id = name;
        }

        public override ImmutableArray<FunctionParameter> Parameters => ImmutableArray<FunctionParameter>.Empty;

        public override string PathToAssembly => "";

        public override string EntryPoint { get; }

        public override string Id { get; }

        public override string Name { get; }

        public override IImmutableDictionary<string, BindingMetadata> InputBindings =>
            ImmutableDictionary<string, BindingMetadata>.Empty;

        public override IImmutableDictionary<string, BindingMetadata> OutputBindings =>
            ImmutableDictionary<string, BindingMetadata>.Empty;
    }

    private sealed class Bindings : BindingContext {
        public Bindings(IReadOnlyDictionary<string, object?> data) {
            BindingData = data;
        }

        public override IReadOnlyDictionary<string, object?> BindingData { get; }
    }

    private sealed class Trace : TraceContext {
        public override string TraceParent => "";

        public override string TraceState => "";
    }

    private sealed class Retry : RetryContext {
        public override int RetryCount => 0;

        public override int MaxRetryCount => 0;
    }

    private sealed class FeatureCollection : IInvocationFeatures {
        private readonly Dictionary<Type, object> _features = new();

        public void Set<T>(T instance) {
            if (instance != null) {
                _features[typeof(T)] = instance;
            }
        }

        public T? Get<T>() => _features.TryGetValue(typeof(T), out var feature) ? (T)feature : default;

        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
