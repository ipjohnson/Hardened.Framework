using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution
{
    public class EmptyParameters : IExecutionRequestParameters
    {
        public bool TryGetParameter(string parameterName, out object? parameterValue)
        {
            parameterValue = null;

            return false;
        }

        public bool TrySetParameter(string parameterName, object parameterValue)
        {
            return false;
        }

        public IReadOnlyList<IExecutionRequestParameter> Info => Array.Empty<IExecutionRequestParameter>();

        public object this[int index]
        {
            get => throw new IndexOutOfRangeException();
            set => throw new IndexOutOfRangeException();
        }

        public int ParameterCount => 0;

        public static IExecutionRequestParameters Instance { get; } = new EmptyParameters();

        public IExecutionRequestParameters Clone()
        {
            return this;
        }
    }
}
