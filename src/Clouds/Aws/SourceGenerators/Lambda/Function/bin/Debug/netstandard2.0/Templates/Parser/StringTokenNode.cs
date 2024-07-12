namespace Hardened.SourceGenerator.Templates.Parser;

public enum StringTokenNodeType
{
    Content,
    Mustache
}

public class StringTokenNode
{
    private readonly string _templateString;
    private readonly int _startIndex;
    private readonly int _endIndex;

    public StringTokenNode(StringTokenNodeType tokenNodeType, string templateString, int startIndex, int endIndex)
    {
        _templateString = templateString;
        _startIndex = startIndex;
        _endIndex = endIndex;
        TokenNodeType = tokenNodeType;
    }

    public StringTokenNodeType TokenNodeType { get; }
        
    public ReadOnlySpan<char> Token => _templateString.AsSpan(_startIndex, _endIndex - _startIndex);
}