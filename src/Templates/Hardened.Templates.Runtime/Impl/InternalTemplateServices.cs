using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

[SingletonService(Using = RegistrationType.Try)]
public class InternalTemplateServices : IInternalTemplateServices {
    public InternalTemplateServices(
        IStringBuilderPool stringBuilderPool,
        IDataFormattingService dataFormattingService,
        ITemplateHelperService templateHelperService,
        IBooleanLogicService booleanLogicService,
        IStringEscapeServiceProvider stringEscapeServiceProvider) {
        StringBuilderPool = stringBuilderPool;
        DataFormattingService = dataFormattingService;
        TemplateHelperService = templateHelperService;
        BooleanLogicService = booleanLogicService;
        StringEscapeServiceProvider = stringEscapeServiceProvider;
    }

    public IStringBuilderPool StringBuilderPool { get; }

    public IDataFormattingService DataFormattingService { get; }

    public ITemplateHelperService TemplateHelperService { get; }

    public IBooleanLogicService BooleanLogicService { get; }

    public IStringEscapeServiceProvider StringEscapeServiceProvider { get; }
}