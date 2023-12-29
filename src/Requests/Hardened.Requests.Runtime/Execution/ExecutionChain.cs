using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution;

public class ExecutionChain : IExecutionChain {
    private readonly IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> _filterChain;
    private int _index;

    public ExecutionChain(IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> filterChain,
        IExecutionContext context) {
        _filterChain = filterChain;
        Context = context;
    }

    private ExecutionChain(IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> filterChain,
        IExecutionContext context, int index) {
        _index = index;
        _filterChain = filterChain;
        Context = context;
    }

    public Task Next() {
        if (_index >= _filterChain.Count) {
            return Task.CompletedTask;
        }

        return _filterChain[_index++](Context).Execute(this);
    }

    public IExecutionContext Context { get; }

    public IExecutionChain Fork(IExecutionContext context) {
        return new ExecutionChain(_filterChain, context, _index);
    }

    public bool IsLastFilter => _index >= _filterChain.Count;
}