using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// What a handler taking no parameters binds to.
/// </summary>
/// <remarks>
/// <para>
/// Every no-parameter handler in every application resolves this, and it was at 22% — the shared
/// instance was constructed and nothing asked it anything.
/// </para>
/// <para>
/// The property worth pinning is <see cref="EmptyParameters.Clone"/> returning <c>this</c>. A
/// forked chain clones the parameters, so a <c>Clone</c> that allocated would allocate once per
/// retry attempt per no-parameter handler; and because the instance is shared process-wide, a
/// <c>Clone</c> that returned something writable would let one request's fork hand state to
/// another's.
/// </para>
/// </remarks>
public class EmptyParametersTests {

    [Fact]
    public void ParameterCountIsZero() {
        Assert.Equal(0, EmptyParameters.Instance.ParameterCount);
    }

    [Fact]
    public void InfoIsEmptyRatherThanNull() {
        Assert.Empty(EmptyParameters.Instance.Info);
    }

    [Fact]
    public void TryGetParameterFindsNothingAndYieldsNull() {
        Assert.False(EmptyParameters.Instance.TryGetParameter("anything", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TrySetParameterRefusesRatherThanThrowing() {
        Assert.False(EmptyParameters.Instance.TrySetParameter("anything", "value"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void IndexingThrowsAtEveryIndex(int index) {
        Assert.Throws<IndexOutOfRangeException>(() => EmptyParameters.Instance[index]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AssigningByIndexThrows(int index) {
        Assert.Throws<IndexOutOfRangeException>(() => EmptyParameters.Instance[index] = "value");
    }

    [Fact]
    public void InstanceIsShared() {
        Assert.Same(EmptyParameters.Instance, EmptyParameters.Instance);
    }

    /// <summary>
    /// A fork clones the parameters. There is nothing to copy, so cloning must not allocate — and
    /// must not hand back something a second request could write into.
    /// </summary>
    [Fact]
    public void CloneIsTheSameInstance() {
        IExecutionRequestParameters parameters = EmptyParameters.Instance;

        Assert.Same(parameters, parameters.Clone());
    }
}
