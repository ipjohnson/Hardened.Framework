using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Tests.Execution;

/// <summary>
/// The by-name indexer <see cref="IExecutionRequestParameters"/> supplies as a default
/// implementation.
/// </summary>
/// <remarks>
/// <para>
/// Generated parameter classes implement the by-index members and inherit this one, so nothing in
/// the framework calls it and nothing in the suite did either - it is what a filter or a
/// hand-written handler reaches for when it knows a parameter's name rather than its position.
/// </para>
/// <para>
/// Both halves throw <c>KeyNotFoundException</c> for a name the class does not carry, and the
/// getter throws for a name it carries with a null value. That last one is the interesting case:
/// the guard is <c>TryGetParameter(...) &amp;&amp; value != null</c>, so a parameter that exists and
/// is null is indistinguishable from one that does not exist, and a caller cannot tell the two
/// apart. Asserted rather than merely covered, because it is a decision.
/// </para>
/// </remarks>
public class RequestParameterIndexerTests {

    private sealed class Parameters : IExecutionRequestParameters {

        private readonly Dictionary<string, object?> _values;

        public Parameters(Dictionary<string, object?> values) => _values = values;

        public bool TryGetParameter(string parameterName, out object? parameterValue) =>
            _values.TryGetValue(parameterName, out parameterValue);

        public bool TrySetParameter(string parameterName, object parameterValue) {
            if (!_values.ContainsKey(parameterName)) {
                return false;
            }

            _values[parameterName] = parameterValue;

            return true;
        }

        public IReadOnlyList<IExecutionRequestParameter> Info => [];

        public object this[int index] {
            get => _values.Values.ElementAt(index)!;
            set => _values[_values.Keys.ElementAt(index)] = value;
        }

        public int ParameterCount => _values.Count;

        public IExecutionRequestParameters Clone() => new Parameters(new Dictionary<string, object?>(_values));
    }

    private static Parameters With(string name, object? value) =>
        new(new Dictionary<string, object?> { [name] = value });

    [Fact]
    public void GettingAKnownParameterReturnsIt() {
        IExecutionRequestParameters parameters = With("id", 42);

        Assert.Equal(42, parameters["id"]);
    }

    [Fact]
    public void GettingAnUnknownParameterThrows() {
        IExecutionRequestParameters parameters = With("id", 42);

        var exception = Assert.Throws<KeyNotFoundException>(() => parameters["missing"]);

        Assert.Contains("missing", exception.Message);
    }

    /// <summary>
    /// A parameter that exists and holds null throws too, rather than returning null.
    /// </summary>
    [Fact]
    public void GettingAParameterHoldingNullThrows() {
        IExecutionRequestParameters parameters = With("id", null);

        Assert.Throws<KeyNotFoundException>(() => parameters["id"]);
    }

    [Fact]
    public void SettingAKnownParameterReplacesIt() {
        IExecutionRequestParameters parameters = With("id", 1);

        parameters["id"] = 2;

        Assert.Equal(2, parameters["id"]);
    }

    [Fact]
    public void SettingAnUnknownParameterThrows() {
        IExecutionRequestParameters parameters = With("id", 1);

        var exception = Assert.Throws<KeyNotFoundException>(() => parameters["missing"] = 2);

        Assert.Contains("missing", exception.Message);
    }
}
