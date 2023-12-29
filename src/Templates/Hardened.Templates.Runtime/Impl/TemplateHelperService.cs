using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class TemplateHelperService : ITemplateHelperService {
    private readonly IEnumerable<ITemplateHelperProvider> _providers;

    public TemplateHelperService(IEnumerable<ITemplateHelperProvider> providers) {
        _providers = providers.Reverse();
    }

    public TemplateHelperFactory LocateHelper(string helperToken) {
        foreach (var helperProvider in _providers) {
            var factor = helperProvider.GetTemplateHelperFactory(helperToken);

            if (factor != null) {
                return factor;
            }
        }

        throw new Exception($"Could not locate token helper for {helperToken}");
    }
}