namespace Hardened.Functions.Testing;

/// <summary>
/// Hands a generated façade the delegate it was compiled against.
/// </summary>
/// <remarks>
/// Everything it does is turn a <see cref="ITriggerDelivery"/> into a
/// <c>Func&lt;object, string, string, Task&gt;</c>, because that is what a façade takes - a
/// delegate of types available everywhere, so an application carrying one references no testing
/// package. Which delivery is behind it is the harness's business and the test's choice of
/// attribute.
/// </remarks>
public sealed class TriggerInvoker {
    private readonly ITriggerDelivery _delivery;

    public TriggerInvoker(ITriggerDelivery delivery) {
        _delivery = delivery;
    }

    public Func<object, string, string, Task> Invoke =>
        (messages, scheme, path) =>
            _delivery.Deliver(
                ((System.Collections.IEnumerable)messages).Cast<object>().ToArray(), scheme, path);

    /// <summary>The delegate an invocation façade is constructed with.</summary>
    public Func<object, string, string, Type?, Task<object?>> Call =>
        (message, scheme, path, responseType) =>
            _delivery.Call(message, scheme, path, responseType);

    /// <summary>Builds the façade for one trigger kind, wired to this invoker.</summary>
    /// <remarks>
    /// An invocation façade takes the answering delegate and every other kind takes the sending
    /// one, so which to hand over is decided by which constructor the generated type declares
    /// rather than by a name convention.
    /// </remarks>
    public TFacade Facade<TFacade>() {
        var answering = typeof(TFacade).GetConstructor(
            [typeof(Func<object, string, string, Type?, Task<object?>>)]);

        return (TFacade)(answering != null
            ? answering.Invoke([Call])
            : Activator.CreateInstance(typeof(TFacade), Invoke)!);
    }
}
