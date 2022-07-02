using System;
using System.Collections.Generic;

namespace Hardened.SourceGenerator.Templates.Parser
{
    public class StringTokenNodeParser
    {
        public class TokenInfo
        {
            public TokenInfo(string startToken, string endToken)
            {
                StartToken = startToken;
                EndToken = endToken;
            }

            public string StartToken { get; }

            public string EndToken { get; }
        }

        private readonly IStringTokenNodeCreatorService _tokenCreatorService;

        public StringTokenNodeParser(IStringTokenNodeCreatorService tokenCreatorService)
        {
            _tokenCreatorService = tokenCreatorService;
        }

        public IList<StringTokenNode> ParseTemplate(string template, TokenInfo tokenInfo)
        {
            var tokens = new List<StringTokenNode>();
            var currentIndex = 0;

            do
            {
                var beginMustache = FindMustacheBeginning(tokenInfo, template, currentIndex);

                if (currentIndex != beginMustache)
                {
                    var tokenEnd = beginMustache;

                    if (beginMustache == -1)
                    {
                        tokenEnd = template.Length;
                    }

                    tokens.Add(ProcessContentToken(tokenInfo, template, currentIndex, tokenEnd));
                }
                
                if (beginMustache >= 0)
                {
                    var endMustache = FindMustacheEnd(tokenInfo, template, beginMustache);

                    tokens.Add(ProcessMustacheToken(tokenInfo, template, beginMustache, endMustache));

                    currentIndex = endMustache;
                }
                else
                {
                    break;
                }
            } while (currentIndex != -1 &&
                     currentIndex < template.Length);
            
            return tokens;
        }

        private StringTokenNode ProcessMustacheToken(TokenInfo tokenInfo, string template, int beginMustache, int endMustache)
        {
            if (endMustache == -1)
            {
                var templateErrorLength = template.Length - beginMustache;

                if (templateErrorLength > 200)
                {
                    templateErrorLength = 200;
                }

                var templateString = template.Substring(beginMustache, templateErrorLength);

                throw new Exception($"Could not find end of mustache token for {templateString}");
            }

            return _tokenCreatorService.CreateMustacheTokenNode(
                template, beginMustache + tokenInfo.StartToken.Length, endMustache);
        }

        private StringTokenNode ProcessContentToken(TokenInfo tokenInfo, string template, int currentIndex, int beginMustache)
        {
            return _tokenCreatorService.CreateContentTokenNode(
                template, currentIndex + tokenInfo.EndToken.Length, beginMustache);
        }

        private int FindMustacheEnd(TokenInfo tokenInfo, string template, int beginMustache)
        {
            return template.IndexOf(tokenInfo.EndToken, beginMustache, StringComparison.CurrentCulture);
        }

        private int FindMustacheBeginning(TokenInfo tokenInfo, string template, int currentIndex)
        {
            return template.IndexOf(tokenInfo.StartToken, currentIndex, StringComparison.CurrentCulture);
        }
    }
}
