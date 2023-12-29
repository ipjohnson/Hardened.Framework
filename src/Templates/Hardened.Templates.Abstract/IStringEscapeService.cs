namespace Hardened.Templates.Abstract;

public interface IStringEscapeService {
    bool CanEscapeTemplate(string templateExtension);

    string EscapeString(string? value);
}