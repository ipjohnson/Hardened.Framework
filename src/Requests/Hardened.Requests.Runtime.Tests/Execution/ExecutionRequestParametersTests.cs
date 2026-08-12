using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// The half of a parameter bag that is no longer generated.
///
/// <para>
/// Every handler's <c>Parameters</c> class derives from this and supplies only the typed
/// properties, the indexer and <see cref="ExecutionRequestParameters.Info"/>. Lookup by name,
/// the count and cloning all come from here, so a defect in any of them is a defect in every
/// handler in an application at once - which is the reason it is worth testing directly rather
/// than through one generated bag that happens to exercise it.
/// </para>
/// </summary>
public class ExecutionRequestParametersTests {

    /// <summary>
    /// Two named slots held in fields, which is the shape the generator emits — one property per
    /// parameter, and an indexer that switches over them. Storing them in a backing array
    /// instead would break <see cref="ExecutionRequestParameters.Clone"/>, for the reason
    /// <see cref="ArrayBackedParameters"/> pins below.
    /// </summary>
    private class TwoParameters : ExecutionRequestParameters {
        public object? Id { get; set; }

        public object? Model { get; set; }

        public override object this[int index] {
            get => index == 0 ? Id! : Model!;
            set {
                if (index == 0) {
                    Id = value;
                }
                else {
                    Model = value;
                }
            }
        }

        public override IReadOnlyList<IExecutionRequestParameter> Info { get; } = [
            new ExecutionRequestParameter("id", 0, typeof(string)),
            new ExecutionRequestParameter("model", 1, typeof(object))
        ];
    }

    /// <summary>Stores its values in a container rather than in fields.</summary>
    private class ArrayBackedParameters : ExecutionRequestParameters {
        private readonly object?[] _values = new object?[1];

        public override object this[int index] {
            get => _values[index]!;
            set => _values[index] = value;
        }

        public override IReadOnlyList<IExecutionRequestParameter> Info { get; } = [
            new ExecutionRequestParameter("only", 0, typeof(object))
        ];
    }

    /// <summary>
    /// Info whose declared indices do not match their position in the list, which is what
    /// separates "the slot this parameter occupies" from "where its description happened to sit".
    /// </summary>
    private class ReorderedInfoParameters : ExecutionRequestParameters {
        public object? Slot0 { get; set; }

        public object? Slot1 { get; set; }

        public override object this[int index] {
            get => index == 0 ? Slot0! : Slot1!;
            set {
                if (index == 0) {
                    Slot0 = value;
                }
                else {
                    Slot1 = value;
                }
            }
        }

        // "second" is described first but occupies slot 1.
        public override IReadOnlyList<IExecutionRequestParameter> Info { get; } = [
            new ExecutionRequestParameter("second", 1, typeof(object)),
            new ExecutionRequestParameter("first", 0, typeof(object))
        ];
    }

    [Fact]
    public void ParameterCountComesFromTheDeclaredParameters() {
        Assert.Equal(2, new TwoParameters().ParameterCount);
    }

    /// <summary>
    /// Lookup follows the declared <see cref="IExecutionRequestParameter.Index"/>, not the
    /// position the description sits at. The generator emits them equal, so this is the
    /// difference between a base class that happens to work and one that is correct by
    /// construction.
    /// </summary>
    [Fact]
    public void LookupUsesTheDeclaredIndexRatherThanThePositionInInfo() {
        var parameters = new ReorderedInfoParameters();

        Assert.True(parameters.TrySetParameter("second", "went to slot one"));

        Assert.Equal("went to slot one", parameters.Slot1);
        Assert.Null(parameters.Slot0);

        Assert.True(parameters.TryGetParameter("second", out var value));
        Assert.Equal("went to slot one", value);
    }

    [Fact]
    public void AKnownNameReadsTheValueAtItsPosition() {
        var parameters = new TwoParameters();

        parameters[1] = "the model";

        Assert.True(parameters.TryGetParameter("model", out var value));
        Assert.Equal("the model", value);
    }

    /// <summary>The first slot as well as a later one — an off-by-one here reads the wrong argument.</summary>
    [Fact]
    public void TheFirstParameterIsReachableByName() {
        var parameters = new TwoParameters();

        parameters[0] = "the id";

        Assert.True(parameters.TryGetParameter("id", out var value));
        Assert.Equal("the id", value);
    }

    [Fact]
    public void AnUnknownNameReadsNothingAndSaysSo() {
        Assert.False(new TwoParameters().TryGetParameter("absent", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void AKnownNameWritesTheValueAtItsPosition() {
        var parameters = new TwoParameters();

        Assert.True(parameters.TrySetParameter("model", "written"));
        Assert.Equal("written", parameters[1]);
    }

    [Fact]
    public void AnUnknownNameWritesNothingAndSaysSo() {
        var parameters = new TwoParameters();

        Assert.False(parameters.TrySetParameter("absent", "ignored"));
        Assert.Null(parameters.TryGetParameter("id", out var value) ? value : null);
    }

    /// <summary>
    /// Name matching is ordinal. A parameter named <c>Model</c> is a different parameter from
    /// <c>model</c>, because that is what C# means by them.
    /// </summary>
    [Fact]
    public void NameMatchingIsCaseSensitive() {
        Assert.False(new TwoParameters().TryGetParameter("Model", out _));
    }

    /// <summary>
    /// The copy carries the values and is independent of the original. This is what
    /// <c>IExecutionRequest.Clone</c> relies on to isolate a forked chain.
    /// </summary>
    [Fact]
    public void CloneCarriesTheValuesAndDetachesFromTheOriginal() {
        var parameters = new TwoParameters();

        parameters[0] = "original id";
        parameters[1] = "original model";

        var clone = (TwoParameters)parameters.Clone();

        Assert.Equal("original id", clone[0]);
        Assert.Equal("original model", clone[1]);

        clone[1] = "rebound";

        Assert.Equal("original model", parameters[1]);
    }

    /// <summary>
    /// The limit of the inherited <c>Clone</c>, pinned so it is a known constraint rather than a
    /// surprise.
    ///
    /// <para>
    /// <c>MemberwiseClone</c> copies fields. A bag whose values live in fields — which is what
    /// the generator emits, one property per parameter — is therefore isolated by it. A bag that
    /// holds its values in an array or list has that container copied by reference, so the copy
    /// writes straight through to the original and the isolation a fork depends on is gone.
    /// </para>
    ///
    /// <para>
    /// Recorded 2026-08-12, found by writing this suite: the first version of the test double
    /// here was array-backed and failed for exactly this reason. Any type deriving from
    /// <see cref="ExecutionRequestParameters"/> that does not store values in fields must
    /// override <c>Clone</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void CloneDoesNotDetachAParametersBagThatStoresItsValuesInAContainer() {
        var parameters = new ArrayBackedParameters();

        parameters[0] = "original";

        var clone = (ArrayBackedParameters)parameters.Clone();

        clone[0] = "rebound";

        Assert.Equal("rebound", parameters[0]);
    }
}
