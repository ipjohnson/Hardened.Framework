using Hardened.Shared.Runtime.Collections;

namespace Hardened.Templates.Abstract;

public interface IInternalTemplateServices
{
    IStringBuilderPool StringBuilderPool { get; }

    IDataFormattingService DataFormattingService { get; }

    ITemplateHelperService TemplateHelperService { get; }

    IBooleanLogicService BooleanLogicService { get; }

    IStringEscapeServiceProvider StringEscapeServiceProvider { get; }
}