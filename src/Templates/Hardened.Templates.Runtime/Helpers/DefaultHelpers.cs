using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Helpers.Collection;
using Hardened.Templates.Runtime.Helpers.String;
using Hardened.Templates.Runtime.Helpers.Url;

namespace Hardened.Templates.Runtime.Helpers;

public partial class DefaultHelpers : ITemplateHelperProvider
{
    private TemplateHelperFactory? _appendHelper;
    private TemplateHelperFactory? _concatHelper;
    private TemplateHelperFactory? _containsHelper;
    private TemplateHelperFactory? _endsWithHelper;
    private TemplateHelperFactory? _formatHelper;
    private TemplateHelperFactory? _joinHelper;
    private TemplateHelperFactory? _replaceHelper;
    private TemplateHelperFactory? _startsWithHelper;
    private TemplateHelperFactory? _splitHelper;
    private TemplateHelperFactory? _substringHelper;
    private TemplateHelperFactory? _trimHelper;
    private TemplateHelperFactory? _toHelper;
    private TemplateHelperFactory? _toLowerHelper;
    private TemplateHelperFactory? _toUpperHelper;

    private TemplateHelperFactory? _decodeHelper;
    private TemplateHelperFactory? _encodeHelper;

    private TemplateHelperFactory? _lookupHelper;
    private TemplateHelperFactory? _renderCollectionHelper;
    private TemplateHelperFactory? _renderHelper;

    public TemplateHelperFactory GetTemplateHelperFactory(string mustacheToken)
    {
        if (mustacheToken.StartsWith("String."))
        {
            switch (mustacheToken)
            {
                case StringHelperToken.Append:
                    return _appendHelper ??= CreateTemplateHelperFactory(new AppendHelper());
                case StringHelperToken.Concat:
                    return _concatHelper ??= CreateTemplateHelperFactory(new ConcatHelper());
                case StringHelperToken.Contains:
                    return _containsHelper ??= CreateTemplateHelperFactory(new ContainsHelper());
                case StringHelperToken.EndsWith:
                    return _endsWithHelper ??= CreateTemplateHelperFactory(new EndsWithHelper());
                case StringHelperToken.Format:
                    return _formatHelper ??= CreateTemplateHelperFactory(new FormatHelper());
                case StringHelperToken.Join:
                    return _joinHelper ??= CreateTemplateHelperFactory(new JoinHelper());
                case StringHelperToken.Replace:
                    return _replaceHelper ??= CreateTemplateHelperFactory(new ReplaceHelper());
                case StringHelperToken.Split:
                    return _splitHelper ??= CreateTemplateHelperFactory(new SplitHelper());
                case StringHelperToken.StartsWith:
                    return _startsWithHelper ??= CreateTemplateHelperFactory(new StartsWithHelper());
                case StringHelperToken.Substring:
                    return _substringHelper ??= CreateTemplateHelperFactory(new SubstringHelper());
                case StringHelperToken.Trim:
                    return _trimHelper ??= CreateTemplateHelperFactory(new TrimHelper());
                case StringHelperToken.To:
                    return _toHelper ??= CreateTemplateHelperFactory(new ToHelper());
                case StringHelperToken.ToLower:
                    return _toLowerHelper ??= CreateTemplateHelperFactory(new ToLowerHelper());
                case StringHelperToken.ToUpper:
                    return _toUpperHelper ??= CreateTemplateHelperFactory(new ToUpperHelper());
            }
        }
        else if (mustacheToken.StartsWith("Url."))
        {
            switch (mustacheToken)
            {
                case UrlHelperTokens.Decode:
                    return _decodeHelper ??= CreateTemplateHelperFactory(new DecodeHelper());
                case UrlHelperTokens.Encode:
                    return _encodeHelper ??= CreateTemplateHelperFactory(new EncodeHelper());
            }
        }
        else
        {
            switch (mustacheToken)
            {
                case CollectionHelperTokens.Lookup:
                    return _lookupHelper ??= CreateTemplateHelperFactory(new LookupHelper());
                case CollectionHelperTokens.RenderCollection:
                    return _renderCollectionHelper ??= CreateTemplateHelperFactory(new RenderCollectionHelper());
                case CollectionHelperTokens.Render:
                    return _renderHelper ??= CreateTemplateHelperFactory(new DynamicTemplateRenderHelper());
            }
        }

        return null;
    }

    public static TemplateHelperFactory CreateTemplateHelperFactory(ITemplateHelper templateHelper)
    {
        return _ => templateHelper;
    }
}