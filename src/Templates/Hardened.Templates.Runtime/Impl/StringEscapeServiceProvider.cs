using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class StringEscapeServiceProvider : IStringEscapeServiceProvider {
    private readonly IEnumerable<IStringEscapeService> _escapeServices;

    public StringEscapeServiceProvider(IEnumerable<IStringEscapeService> escapeServices) {
        _escapeServices = escapeServices.Reverse();
    }

    public IStringEscapeService GetEscapeService(string templateExtension) {
        foreach (var escapeService in _escapeServices) {
            if (escapeService.CanEscapeTemplate(templateExtension)) {
                return escapeService;
            }
        }

        throw new Exception($"Could not find escape service for extension {templateExtension}");
    }
}