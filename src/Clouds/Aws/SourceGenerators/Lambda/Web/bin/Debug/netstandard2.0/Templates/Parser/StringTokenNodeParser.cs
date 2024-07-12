namespace Hardened.SourceGenerator.Templates.Parser;

public class StringTokenNodeParser {
    public class TokenInfo {
        public TokenInfo(string startToken, string endToken) {
            StartToken = startToken;
            EndToken = endToken;
            RawStartToken = startToken + startToken.Last();
            RawEndToken = endToken.First() + endToken;
        }

        public string StartToken { get; }

        public string RawStartToken { get; }

        public string EndToken { get; }

        public string RawEndToken { get; }
    }

    private readonly IStringTokenNodeCreatorService _tokenCreatorService;

    public StringTokenNodeParser(IStringTokenNodeCreatorService tokenCreatorService) {
        _tokenCreatorService = tokenCreatorService;
    }

    public IList<StringTokenNode> ParseTemplate(string template, TokenInfo tokenInfo) {
        var tokens = new List<StringTokenNode>();
        var currentIndex = 0;

        do {
            var beginMustache = FindMustacheBeginning(tokenInfo, template, currentIndex);

            if (currentIndex != beginMustache) {
                var tokenEnd = beginMustache;

                if (beginMustache == -1) {
                    tokenEnd = template.Length;
                }

                tokens.Add(ProcessContentToken(tokenInfo, template, currentIndex, tokenEnd));
            }

            if (beginMustache >= 0) {
                var isRaw = IsRawMustache(template, beginMustache, tokenInfo);

                var endMustache = FindMustacheEnd(tokenInfo, template, beginMustache, isRaw);

                tokens.Add(ProcessMustacheToken(tokenInfo, template, beginMustache, endMustache, isRaw));

                currentIndex = endMustache + (isRaw ? tokenInfo.RawEndToken.Length : tokenInfo.EndToken.Length);
            }
            else {
                break;
            }
        } while (currentIndex != -1 &&
                 currentIndex < template.Length);

        return tokens;
    }

    private bool IsRawMustache(string template, int beginMustache, TokenInfo tokenInfo) {
        if (beginMustache + tokenInfo.RawStartToken.Length > template.Length) {
            return false;
        }

        for (var i = tokenInfo.StartToken.Length; i < tokenInfo.RawStartToken.Length; i++) {
            if (template[beginMustache + i] != tokenInfo.RawStartToken[i]) {
                return false;
            }
        }

        return true;
    }

    private StringTokenNode ProcessMustacheToken(TokenInfo tokenInfo, string template, int beginMustache,
        int endMustache, bool isRaw) {
        if (endMustache == -1) {
            var templateErrorLength = template.Length - beginMustache;

            if (templateErrorLength > 200) {
                templateErrorLength = 200;
            }

            var templateString = template.Substring(beginMustache, templateErrorLength);

            throw new Exception($"Could not find end of mustache token for {templateString}");
        }

        var tokenLength = isRaw ? tokenInfo.RawStartToken.Length : tokenInfo.StartToken.Length;

        return _tokenCreatorService.CreateMustacheTokenNode(
            template, beginMustache + tokenLength, endMustache, isRaw);
    }

    private StringTokenNode ProcessContentToken(TokenInfo tokenInfo, string template, int currentIndex,
        int beginMustache) {
        return _tokenCreatorService.CreateContentTokenNode(
            template, currentIndex, beginMustache);
    }

    private int FindMustacheEnd(TokenInfo tokenInfo, string template, int beginMustache, bool isRaw) {
        return template.IndexOf(
            isRaw ? tokenInfo.RawEndToken : tokenInfo.EndToken,
            beginMustache,
            StringComparison.CurrentCulture);
    }

    private int FindMustacheBeginning(TokenInfo tokenInfo, string template, int currentIndex) {
        return template.IndexOf(tokenInfo.StartToken, currentIndex, StringComparison.CurrentCulture);
    }
}