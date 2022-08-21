using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.PathTokens
{
    public interface IPathTokenCollection
    {
        int Count { get; }

        PathToken Get(int index);

        PathToken? Get(string id);
    }
}
