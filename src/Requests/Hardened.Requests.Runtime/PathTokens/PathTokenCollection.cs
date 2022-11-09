using Hardened.Requests.Abstract.PathTokens;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.PathTokens;

public class PathTokenCollection : IPathTokenCollection
{
    private readonly PathToken[] _pathTokens;

    public static readonly PathTokenCollection Empty = new PathTokenCollection(0);

    public PathTokenCollection(int count, PathToken? lastToken = null)
    {
        _pathTokens = new PathToken[count];

        if (lastToken != null)
        {
            _pathTokens[count - 1] = lastToken;
        }
    }

    public int Count => _pathTokens.Length;

    public PathToken Get(int index)
    {
        return _pathTokens[index];
    }

    public StringValues Get(string id)
    {
        for (var i = 0; i < _pathTokens.Length; i++)
        {
            if (_pathTokens[i]?.TokenName == id)
            {
                return _pathTokens[i].TokenValue;
            }
        }

        return StringValues.Empty;
    }

    public void Set(int index, PathToken pathToken)
    {
        if (index >= _pathTokens.Length)
        {
            throw new Exception($"Index {index} is greater than expected path token length {_pathTokens.Length}");
        }
        
        _pathTokens[index] = pathToken;
    }
}