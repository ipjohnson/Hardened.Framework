using Hardened.Requests.Abstract.PathTokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Runtime.PathTokens
{
    public class PathTokenCollection : IPathTokenCollection
    {
        private readonly PathToken[] _pathTokens;

        public static readonly IPathTokenCollection Empty = new PathTokenCollection(0);

        public PathTokenCollection(int count)
        {
            _pathTokens = new PathToken[count];
        }

        public int Count => _pathTokens.Length;

        public PathToken Get(int index)
        {
            return _pathTokens[index];
        }

        public PathToken? Get(string id)
        {
            for (var i = 0; i < _pathTokens.Length; i++)
            {
                if (_pathTokens[i]?.TokenName == id)
                {
                    return _pathTokens[i];
                }
            }

            return null;
        }

        public void Set(int index, PathToken pathToken)
        {
            _pathTokens[index] = pathToken;
        }
    }
}
