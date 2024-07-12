using System.Text;

namespace Hardened.SourceGenerator.Web.Routing;

public class RouteTreeGenerator<T> {
    private CancellationToken _cancellationToken;

    public RouteTreeGenerator(CancellationToken? cancellationToken = null) {
        _cancellationToken = cancellationToken ?? CancellationToken.None;
    }

    public class Entry {
        public Entry(string pathTemplate, string method, T value) {
            (PathTemplate, WildCardTokens) = StandardizeToken(pathTemplate);
            Method = method.ToUpperInvariant();
            Value = value;
        }

        public string PathTemplate { get; }

        public string Method { get; }

        public IReadOnlyList<string> WildCardTokens { get; }

        public T Value { get; }
    }

    public RouteTreeNode<T> GenerateTree(List<Entry> entries) {
        if (entries.Any(e => e.PathTemplate.First() != '/')) {
            throw new Exception("All paths must start with '/' ");
        }

        entries.Sort(((x, y) => string.Compare(x.PathTemplate, y.PathTemplate, StringComparison.Ordinal)));

        return ProcessEntries("/", entries, 1, 0);
    }

    private RouteTreeNode<T> ProcessEntries(string path, List<Entry> entries, int stringIndex, int wildCardDepth) {
        _cancellationToken.ThrowIfCancellationRequested();

        var longestMatch = LongestCharacterMatch(entries, stringIndex);

        if (longestMatch > 0) {
            return new RouteTreeNode<T>(path,
                new[] { ProcessLongMatchingNodes(entries, stringIndex, longestMatch, wildCardDepth) },
                Array.Empty<RouteTreeNode<T>>(),
                Array.Empty<RouteTreeLeafNode<T>>(),
                wildCardDepth
            );
        }

        return ProcessSingleCharacterNodes(path, entries, stringIndex, wildCardDepth);
    }

    private RouteTreeNode<T> ProcessSingleCharacterNodes(string path, List<Entry> entries, int stringIndex,
        int wildCardDepth) {
        _cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<RouteTreeLeafNode<T>> leafNodes = Array.Empty<RouteTreeLeafNode<T>>();
        var childNodes = new List<RouteTreeNode<T>>();
        IReadOnlyList<RouteTreeNode<T>> wildCardNodes = Array.Empty<RouteTreeNode<T>>();

        var groupings = GroupByLetter(entries, stringIndex);

        foreach (var grouping in groupings) {
            switch (grouping.Key) {
                case '\0':
                    leafNodes = CreateLeafNodes(grouping.Value, stringIndex);
                    break;

                case '{':
                    wildCardNodes = ProcessWildCardNodes(grouping.Value, stringIndex, wildCardDepth + 1);
                    break;

                default:
                    childNodes.Add(ProcessEntries(grouping.Key.ToString(), grouping.Value, stringIndex + 1,
                        wildCardDepth));
                    break;
            }
        }

        childNodes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        return new RouteTreeNode<T>(path,
            childNodes,
            wildCardNodes,
            leafNodes,
            wildCardDepth
        );
    }

    private RouteTreeNode<T> ProcessLongMatchingNodes(List<Entry> entries, int stringIndex, int longestMatch,
        int wildCardDepth) {
        var matchPath = entries[0].PathTemplate.Substring(stringIndex, longestMatch);

        return ProcessEntries(matchPath, entries, stringIndex + longestMatch, wildCardDepth);
    }

    private IReadOnlyList<RouteTreeNode<T>> ProcessWildCardNodes(List<Entry> keyValuePair, int stringIndex,
        int wildCardDepth) {
        var token = keyValuePair.First().WildCardTokens[wildCardDepth - 1];
        stringIndex += "{TOKEN}".Length;

        var returnList = new List<RouteTreeNode<T>>();
        var grouping = GroupByLetter(keyValuePair, stringIndex);

        foreach (var group in grouping) {
            _cancellationToken.ThrowIfCancellationRequested();

            returnList.Add(ProcessEntries(group.Key.ToString(), group.Value, stringIndex + 1, wildCardDepth));
        }

        returnList.ForEach(n => n.WildCardToken = token);

        return returnList;
    }

    private IReadOnlyList<RouteTreeLeafNode<T>> CreateLeafNodes(List<Entry> entries, int stringIndex) {
        var leafNodes = new List<RouteTreeLeafNode<T>>();

        foreach (var entry in entries) {
            _cancellationToken.ThrowIfCancellationRequested();

            leafNodes.Add(new RouteTreeLeafNode<T>(entry.Method, entry.Value));
        }

        return leafNodes;
    }

    private int LongestCharacterMatch(List<Entry> entries, int stringIndex) {
        if (entries.Count == 0) {
            return 0;
        }

        int matchLength = 0;
        char currentChar = '\0';

        do {
            _cancellationToken.ThrowIfCancellationRequested();

            foreach (var entry in entries) {
                if (entry.PathTemplate.Length > (stringIndex + matchLength)) {
                    if (currentChar == '\0') {
                        if (entry.PathTemplate[stringIndex + matchLength] == '{') {
                            return matchLength;
                        }

                        currentChar = entry.PathTemplate[stringIndex + matchLength];
                    }
                    else if (currentChar != entry.PathTemplate[stringIndex + matchLength]) {
                        return matchLength;
                    }
                }
                else {
                    return matchLength;
                }
            }

            currentChar = '\0';
            matchLength++;
        } while (true);
    }

    private Dictionary<char, List<Entry>> GroupByLetter(List<Entry> entries, int stringIndex) {
        var returnValue = new Dictionary<char, List<Entry>>();

        foreach (var entry in entries) {
            _cancellationToken.ThrowIfCancellationRequested();

            char charEntry = '\0';

            if (entry.PathTemplate.Length > stringIndex) {
                charEntry = entry.PathTemplate[stringIndex];
            }

            if (!returnValue.TryGetValue(charEntry, out var groupedEntries)) {
                groupedEntries = new List<Entry>();
                returnValue[charEntry] = groupedEntries;
            }

            groupedEntries.Add(entry);
        }

        return returnValue;
    }

    public static (string, IReadOnlyList<string>) StandardizeToken(string pathTemplate) {
        var tokenIndex = pathTemplate.IndexOf('{');
        var tokenList = new List<string>();

        if (tokenIndex > 0) {
            var stringBuilder = new StringBuilder();
            var currentIndex = 0;
            while (tokenIndex > 0) {
                var tokenEnd = pathTemplate.IndexOf('}', tokenIndex);

                if (tokenEnd > 0) {
                    var length = tokenIndex - currentIndex;
                    stringBuilder.Append(pathTemplate.Substring(currentIndex, length).ToLower());
                    stringBuilder.Append("{TOKEN}");

                    var startIndex = tokenIndex + 1;
                    tokenList.Add(pathTemplate.Substring(startIndex, tokenEnd - startIndex));

                    currentIndex = tokenEnd + 1;
                }

                tokenIndex = pathTemplate.IndexOf('{', tokenIndex + 1);
            }

            if (currentIndex < pathTemplate.Length) {
                stringBuilder.Append(pathTemplate.Substring(currentIndex).ToLower());
            }

            return (stringBuilder.ToString(), tokenList);
        }

        return (pathTemplate, Array.Empty<string>());
    }
}