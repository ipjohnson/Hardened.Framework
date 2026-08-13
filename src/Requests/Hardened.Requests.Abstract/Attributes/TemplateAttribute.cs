namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// Names the view a handler's result should be rendered through.
/// </summary>
/// <remarks>
/// <para>
/// <b>An annotation, not a renderer.</b> The generator reads this as response information rather
/// than as a filter, so it never reaches the handler's metadata array, and puts the name on
/// <c>IExecutionResponse.TemplateName</c> before the handler runs. Nothing in Hardened reads it
/// back: the Mustache engine that used to was removed in #101 because it had no users and was
/// making every application fail eager container validation.
/// </para>
/// <para>
/// So what this buys today is a place to say which view a handler meant, carried to somewhere a
/// renderer can reach it. Anything that wants to act on it - a filter ordered after the handler, a
/// serializer, an engine added later - reads the response property. A handler can also overwrite it
/// to choose a view per request.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class TemplateAttribute : Attribute {
    public TemplateAttribute(string templateName) {
        TemplateName = templateName;
    }

    public string TemplateName { get; }
}
