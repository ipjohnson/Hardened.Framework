namespace Hardened.SourceGenerator.Templates.Parser;

public interface IStringTokenNodeCreatorService {
    StringTokenNode CreateContentTokenNode(string templateString, int startIndex, int endIndex);

    StringTokenNode CreateMustacheTokenNode(string templateString, int startIndex, int endIndex, bool isRaw);
}

public class StringTokenNodeCreatorService : IStringTokenNodeCreatorService {
    public StringTokenNode CreateContentTokenNode(string templateString, int startIndex, int endIndex) {
        return new StringTokenNode(
            StringTokenNodeType.Content,
            templateString,
            startIndex,
            endIndex
        );
    }

    public StringTokenNode CreateMustacheTokenNode(string templateString, int startIndex, int endIndex, bool isRaw) {
        // trim white space from front of token
        while (templateString[startIndex] == ' ' && startIndex < endIndex) {
            startIndex++;
        }

        // trim white space from end of token
        while (templateString[endIndex] == ' ' && endIndex > startIndex) {
            endIndex--;
        }

        return new StringTokenNode(
            isRaw ? StringTokenNodeType.RawMustache : StringTokenNodeType.Mustache,
            templateString,
            startIndex,
            endIndex);
    }
}