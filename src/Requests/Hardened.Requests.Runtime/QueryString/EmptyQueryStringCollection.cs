using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.QueryString
{
    public class EmptyQueryStringCollection : IQueryStringCollection
    {
        public static IQueryStringCollection Instance { get; } = new EmptyQueryStringCollection();

        public int Count => 0;

        public StringValues Get(string key)
        {
            return StringValues.Empty;
        }
    }
}
