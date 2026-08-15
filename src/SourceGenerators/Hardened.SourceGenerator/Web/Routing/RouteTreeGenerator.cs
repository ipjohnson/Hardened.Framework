using System.Text;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Web.Routing;

public class RouteTreeGenerator<T> {
    private CancellationToken _cancellationToken;

    public RouteTreeGenerator(CancellationToken? cancellationToken = null) {
        _cancellationToken = cancellationToken ?? CancellationToken.None;
    }

    public class Entry {
        public Entry(string pathTemplate, string method, T value, bool caseInsensitive = false) {
            (PathTemplate, WildCardTokens) = StandardizeToken(pathTemplate, caseInsensitive);
            Method = method.ToUpperInvariant();
            Value = value;
        }

        public string PathTemplate { get; }

        public string Method { get; }

        /// <summary>
        /// The token names as written, asterisk included — <c>{*path}</c> is kept here as
        /// <c>"*path"</c>. The marker travels with the name because that is the only place the
        /// route's own declaration survives into the tree; strip it with
        /// <see cref="RouteTokens.Name"/> wherever the name is used to bind.
        /// </summary>
        public IReadOnlyList<string> WildCardTokens { get; }

        public T Value { get; }
    }

    public RouteTreeNode<T> GenerateTree(List<Entry> entries) {
        foreach (var entry in entries) {
            var firstChar = entry.PathTemplate.FirstOrDefault();

            if (firstChar != '/') {
                throw new Exception($"All paths must start with '/' but started with '{firstChar}'  entry {entry.PathTemplate} {entry.Method}");
            }
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

        // Taken across every route through this position rather than from the first, because the
        // node is shared and they may not agree. See RouteTreeNode.WildCardIsCatchAll.
        var catchAll = keyValuePair.Any(entry => RouteTokens.IsCatchAll(entry.WildCardTokens, wildCardDepth));

        stringIndex += "{TOKEN}".Length;

        var returnList = new List<RouteTreeNode<T>>();
        var grouping = GroupByLetter(keyValuePair, stringIndex);

        foreach (var group in grouping) {
            _cancellationToken.ThrowIfCancellationRequested();

            var node = ProcessEntries(group.Key.ToString(), group.Value, stringIndex + 1, wildCardDepth);

            node.WildCardToken = token;
            node.WildCardIsCatchAll = catchAll;

            // Per continuation rather than across the whole position: /users/{id:int} and
            // /users/{id}/posts become two nodes here - grouped by what follows the token - and only
            // routes sharing a continuation have to agree. Two that do and disagree is HRDR001,
            // reported before this runs.
            node.WildCardConstraint = RouteTokens.Constraint(group.Value[0].WildCardTokens, wildCardDepth);

            returnList.Add(node);
        }

        return returnList;
    }

    private IReadOnlyList<RouteTreeLeafNode<T>> CreateLeafNodes(List<Entry> entries, int stringIndex) {
        var leafNodes = new List<RouteTreeLeafNode<T>>();

        foreach (var entry in entries) {
            _cancellationToken.ThrowIfCancellationRequested();

            leafNodes.Add(new RouteTreeLeafNode<T>(entry.Method, entry.Value, entry.WildCardTokens));
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

    /// <summary>
    /// The path template with its tokens replaced by a fixed marker, and the token names in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="caseInsensitive"/> lowercases the literal parts, and is what a
    /// case-insensitive matcher needs: it compares each character against both cases, and the tree
    /// it walks has to be in the case it expects.
    /// </para>
    /// <para>
    /// Off by default, because matching is case-sensitive by default - paths are case-sensitive per
    /// RFC 3986. This is the trap in flipping the matcher: lowercasing here while comparing exactly
    /// there makes <c>/Orders</c> match only <c>/orders</c>, silently, for every mixed-case route in
    /// an application. The two have to move together.
    /// </para>
    /// </remarks>
    public static (string, IReadOnlyList<string>) StandardizeToken(
        string pathTemplate, bool caseInsensitive = false) {
        var tokenIndex = pathTemplate.IndexOf('{');
        var tokenList = new List<string>();

        if (tokenIndex > 0) {
            var stringBuilder = new StringBuilder();
            var currentIndex = 0;
            while (tokenIndex > 0) {
                var tokenEnd = pathTemplate.IndexOf('}', tokenIndex);

                if (tokenEnd > 0) {
                    var length = tokenIndex - currentIndex;
                    stringBuilder.Append(Literal(pathTemplate.Substring(currentIndex, length), caseInsensitive));
                    stringBuilder.Append("{TOKEN}");

                    var startIndex = tokenIndex + 1;
                    tokenList.Add(pathTemplate.Substring(startIndex, tokenEnd - startIndex));

                    currentIndex = tokenEnd + 1;
                }

                tokenIndex = pathTemplate.IndexOf('{', tokenIndex + 1);
            }

            if (currentIndex < pathTemplate.Length) {
                stringBuilder.Append(Literal(pathTemplate.Substring(currentIndex), caseInsensitive));
            }

            return (stringBuilder.ToString(), tokenList);
        }

        return (Literal(pathTemplate, caseInsensitive), Array.Empty<string>());
    }

    private static string Literal(string value, bool caseInsensitive) =>
        caseInsensitive ? value.ToLowerInvariant() : value;
}