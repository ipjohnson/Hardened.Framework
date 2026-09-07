using Hardened.Requests.Abstract.Execution;

namespace Hardened.Functions.Testing;

/// <summary>
/// Builds a generated façade and gives it the delegate it was compiled against.
/// </summary>
/// <remarks>
/// Which delegate is decided by which constructor the façade declares, not by a name convention:
/// an invocation façade answers and takes <see cref="TriggerCall"/>, every other kind takes
/// <see cref="TriggerSend"/>. Because those are named types rather than <c>Func</c> shapes,
/// <see cref="Constructor"/> is also how the harness recognises a façade at all - nothing else has
/// a constructor taking one.
/// </remarks>
public sealed class TriggerInvoker {
    private readonly ITriggerDelivery _delivery;

    public TriggerInvoker(ITriggerDelivery delivery) {
        _delivery = delivery;
    }

    /// <summary>
    /// The façade constructor for <paramref name="type"/>, or null when it is not a façade.
    /// </summary>
    /// <remarks>
    /// The whole of the recognition rule. A type either declares a constructor taking one of these
    /// two delegates or it does not, so there is nothing to match approximately and nothing that
    /// can be constructed by accident.
    /// </remarks>
    public static System.Reflection.ConstructorInfo? Constructor(Type type) =>
        type.GetConstructor([typeof(TriggerCall)]) ?? type.GetConstructor([typeof(TriggerSend)]);

    public TriggerSend Send =>
        (messages, scheme, path) =>
            _delivery.Deliver(
                ((System.Collections.IEnumerable)messages).Cast<object>().ToArray(), scheme, path);

    public TriggerCall Call =>
        (message, scheme, path, responseType) => _delivery.Call(message, scheme, path, responseType);

    /// <summary>Builds the façade for one trigger kind, wired to this invoker.</summary>
    public object Facade(Type type) {
        var constructor = Constructor(type)
                          ?? throw new InvalidOperationException(
                              $"{type.Name} is not a trigger façade: it declares no constructor " +
                              $"taking {nameof(TriggerSend)} or {nameof(TriggerCall)}.");

        return constructor.GetParameters()[0].ParameterType == typeof(TriggerCall)
            ? constructor.Invoke([Call])
            : constructor.Invoke([Send]);
    }

    public TFacade Facade<TFacade>() => (TFacade)Facade(typeof(TFacade));
}
