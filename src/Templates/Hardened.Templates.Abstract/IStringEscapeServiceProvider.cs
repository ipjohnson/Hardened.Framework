namespace Hardened.Templates.Abstract;

public interface IStringEscapeServiceProvider
{
    IStringEscapeService GetEscapeService(string templateExtension);
}