using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;

/// <summary>
/// An <see cref="IExecutionChain"/> whose <see cref="Next"/> runs a delegate against the chain's
/// own context.
///
/// <para>
/// The batch filters fork the chain once per record and then read the forked context's response
/// status back, so a fork has to carry the context it was given rather than the one it was forked
/// from. Getting that wrong is invisible to a test that only counts failures — every record would
/// report the outcome of the last one.
/// </para>
/// </summary>
public sealed class TestExecutionChain : IExecutionChain {
    private readonly Func<IExecutionContext, Task> _onNext;

    public TestExecutionChain(IExecutionContext context, Func<IExecutionContext, Task>? onNext = null) {
        Context = context;
        _onNext = onNext ?? (_ => Task.CompletedTask);
    }

    public IExecutionContext Context { get; }

    public bool IsLastFilter => false;

    public Task Next() {
        return _onNext(Context);
    }

    public IExecutionChain Fork(IExecutionContext context) {
        return new TestExecutionChain(context, _onNext);
    }
}
