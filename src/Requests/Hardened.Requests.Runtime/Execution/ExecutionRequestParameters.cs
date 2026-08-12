using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

/// <summary>
/// The half of <see cref="IExecutionRequestParameters"/> that does not need generating.
///
/// <para>
/// A generated parameter bag only has to supply what the compiler knows and this class cannot:
/// the typed properties, the <c>this[int]</c> accessor that reads and writes them without
/// boxing, and <see cref="Info"/>. Everything else — lookup by name, the count, and cloning —
/// follows from those and is implemented once here rather than emitted per handler.
/// </para>
///
/// <para>
/// That is worth roughly half the IL of every generated <c>Parameters</c> class: measured
/// against the web integration app on 2026-08-12, <c>TryGetParameter</c>,
/// <c>TrySetParameter</c>, <c>Clone</c>, <c>ParameterCount</c> and <c>Info</c> accounted for
/// 2,454 of 4,914 bytes across its twenty parameter bags.
/// </para>
/// </summary>
public abstract class ExecutionRequestParameters : IExecutionRequestParameters {

    /// <summary>Reads and writes the typed backing properties. Generated.</summary>
    public abstract object this[int index] { get; set; }

    /// <summary>
    /// The parameters this handler declares, in declaration order. Generated, and expected to
    /// be a static array shared by every instance.
    /// </summary>
    public abstract IReadOnlyList<IExecutionRequestParameter> Info { get; }

    public int ParameterCount => Info.Count;

    public bool TryGetParameter(string parameterName, out object? parameterValue) {
        var index = IndexOf(parameterName);

        if (index < 0) {
            parameterValue = null;

            return false;
        }

        parameterValue = this[index];

        return true;
    }

    public bool TrySetParameter(string parameterName, object parameterValue) {
        var index = IndexOf(parameterName);

        if (index < 0) {
            return false;
        }

        this[index] = parameterValue;

        return true;
    }

    /// <summary>
    /// A shallow copy, which is what an isolated fork needs: the values are handed to the copy,
    /// and rebinding one no longer writes through to the other.
    ///
    /// <para>
    /// Shallow deliberately. The generated version this replaces assigned each property across,
    /// which is the same thing — the objects a parameter refers to were never copied, and a
    /// filter that mutates a bound model still shares it. Deep-copying arbitrary handler
    /// arguments is not something this layer can do safely.
    /// </para>
    /// </summary>
    public virtual IExecutionRequestParameters Clone() =>
        (IExecutionRequestParameters)MemberwiseClone();

    /// <summary>
    /// Position of <paramref name="parameterName"/>, or -1.
    ///
    /// <para>
    /// A scan rather than a generated switch. Both sides are compile-time literals, so the
    /// runtime interns them and <see cref="string.Equals(string, string, StringComparison)"/>
    /// settles each candidate on a reference comparison. Measured on 2026-08-12 that beat the
    /// switch the generator used to emit at every parameter count up to five — 1.20ns against
    /// 1.59ns at two, 2.02ns against 2.26ns at five — while removing the per-handler code
    /// entirely.
    /// </para>
    ///
    /// <para>
    /// Linear, so it will lose to a hash somewhere well above the one to three parameters real
    /// handlers declare. Virtual so that a handler with an unusual number of them can override
    /// it, or so this can grow a threshold later, without touching what the generator emits.
    /// </para>
    /// </summary>
    protected virtual int IndexOf(string parameterName) {
        var info = Info;

        for (var i = 0; i < info.Count; i++) {
            if (string.Equals(info[i].Name, parameterName, StringComparison.Ordinal)) {
                return i;
            }
        }

        return -1;
    }
}
