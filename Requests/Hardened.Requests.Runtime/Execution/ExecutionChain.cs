using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution
{
    public class ExecutionChain : IExecutionChain
    {
        private readonly IReadOnlyList<IExecutionFilter> _filterChain;
        private int _index;

        public ExecutionChain(IReadOnlyList<IExecutionFilter> filterChain, IExecutionContext context)
        {
            _filterChain = filterChain;
            Context = context;
        }

        private ExecutionChain(IReadOnlyList<IExecutionFilter> filterChain, IExecutionContext context, int index)
        {
            _index = index;
            _filterChain = filterChain;
            Context = context;
        }

        public Task Next()
        {
            if (_index >= _filterChain.Count)
            {
                return Task.CompletedTask;
            }

            var filter = _filterChain[_index++];

            return filter.Execute(this);
        }

        public IExecutionContext Context { get; }

        public IExecutionChain Fork(IExecutionContext context)
        {
            return new ExecutionChain(_filterChain, context, _index);
        }
    }
}
