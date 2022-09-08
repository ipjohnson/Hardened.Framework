using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.QueryString
{
    public interface IQueryStringCollection
    {
        int Count { get; }

        StringValues Get(string key);
    }
}
