namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionRequestParameter
    {
        string Name { get; }

        Type Type { get; }
    }

    public interface IExecutionRequestParameters
    {
        /// <summary>
        /// Try getting parameter by name
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="parameterValue"></param>
        /// <returns></returns>
        bool TryGetParameter(string parameterName, out object? parameterValue);

        /// <summary>
        /// Try setting parameter value by name
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="parameterValue"></param>
        /// <returns></returns>
        bool TrySetParameter(string parameterName, object parameterValue);

        /// <summary>
        /// List of parameter info object for the call
        /// </summary>
        IReadOnlyList<IExecutionRequestParameter> ParameterInfos { get; }

        /// <summary>
        /// Access parameters based on index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        object this[int index] { get; set; }

        /// <summary>
        /// Count of parameters
        /// </summary>
        int ParameterCount { get; }

        /// <summary>
        /// Clone parameters
        /// </summary>
        /// <returns></returns>
        IExecutionRequestParameters Clone();

        /// <summary>
        /// Access parameters by name
        /// </summary>
        /// <param name="parameterName"></param>
        /// <returns></returns>
        object this[string parameterName]
        {
            get
            {
                if (TryGetParameter(parameterName, out var value) && value != null)
                {
                    return value;
                }

                throw new KeyNotFoundException($"Parameter context does not have parameter named {parameterName}");
            }
            set
            {
                if (!TrySetParameter(parameterName, value))
                {
                    throw new KeyNotFoundException($"Parameter context does not have parameter named {parameterName}");
                }
            }
        }
    }
}
