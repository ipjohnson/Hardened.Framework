using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class TemplateExecutionService : ITemplateExecutionService {
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly IReadOnlyList<ITemplateExecutionHandlerProvider> _handlerProviders;

    public TemplateExecutionService(IEnumerable<ITemplateExecutionHandlerProvider> handlerProviders,
        IStringBuilderPool stringBuilderPool) {
        _stringBuilderPool = stringBuilderPool;
        _handlerProviders = new List<ITemplateExecutionHandlerProvider>(handlerProviders.Reverse());

        foreach (var templateExecutionHandlerProvider in _handlerProviders) {
            templateExecutionHandlerProvider.TemplateExecutionService = this;
        }
    }

    public async Task<string> Execute(string templateName, object templateData, IServiceProvider serviceProvider) {
        using var handle = _stringBuilderPool.Get();

        await Execute(templateName, templateData, serviceProvider,
            new StringBuilderTemplateOutputWriter(handle.Item), null, null);

        return handle.Item.ToString();
    }

    public Task Execute(
        string templateName,
        object? templateData,
        IServiceProvider serviceProvider,
        ITemplateOutputWriter writer,
        ITemplateExecutionContext? parentContext,
        IExecutionContext? executionContext) {
        // don't run template if data is null
        if (templateData == null) {
            return Task.CompletedTask;
        }

        var templateExecutionFunction = FindTemplateExecutionFunction(templateName);

        if (templateExecutionFunction == null) {
            throw new Exception($"Could not locate template named {templateName}");
        }

        return templateExecutionFunction(templateData, serviceProvider, writer, parentContext, executionContext);
    }

    public TemplateExecutionFunction? FindTemplateExecutionFunction(string templateName) {
        foreach (var provider in _handlerProviders) {
            var handler = provider.GetTemplateExecutionHandler(templateName);

            if (handler != null) {
                return handler.Execute;
            }
        }

        return null;
    }
}