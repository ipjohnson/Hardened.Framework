namespace Hardened.Requests.Abstract.PathTokens;

public class PathToken
{
    public PathToken(string tokenName, string tokenValue)
    {
        TokenName = tokenName;
        TokenValue = tokenValue;
    }

    public string TokenName { get; }

    public string TokenValue { get; }
}