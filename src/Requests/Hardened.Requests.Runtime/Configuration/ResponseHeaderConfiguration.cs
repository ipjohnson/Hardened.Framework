using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Configuration;

public interface IResponseHeaderConfiguration
{
    IReadOnlyList<KeyValuePair<string,StringValues>> CommonHeaders { get; }

    IReadOnlyList<Action<IExecutionContext>> HeaderActions { get; }
}

public class ResponseHeaderConfiguration : IResponseHeaderConfiguration
{
    public List<KeyValuePair<string, StringValues>> CommonHeaders { get; } = new ();

    public List<Action<IExecutionContext>> HeaderActions { get; } = new();

    public void Add(string name, string value)
    {
        Add(name, new StringValues(value));
    }

    public void Add(string name, StringValues value)
    {
        CommonHeaders.Add(new KeyValuePair<string, StringValues>(name, value));
    }

    public void Add(Action<IExecutionContext> action)
    {
        HeaderActions.Add(action);
    }

    IReadOnlyList<KeyValuePair<string, StringValues>> IResponseHeaderConfiguration.CommonHeaders => CommonHeaders;

    IReadOnlyList<Action<IExecutionContext>> IResponseHeaderConfiguration.HeaderActions => HeaderActions;
}