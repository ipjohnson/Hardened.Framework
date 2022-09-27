using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.PathTokens
{
    public interface IPathTokenCollection
    {
        int Count { get; }

        PathToken Get(int index);

        StringValues Get(string id);
    }
}
