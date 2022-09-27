using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.QueryString
{
    public interface IQueryStringCollection
    {
        int Count { get; }

        StringValues Get(string key);
    }
}
