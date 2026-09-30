using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Hardened.Web.Testing;

/// <summary>
/// The field errors a validation refusal carried, for the message of a failure about it.
/// </summary>
/// <remarks>
/// A test expecting a 2xx that was answered 400 wants to know which field failed, and the body the
/// expectation already read says so. The text is read where the route kept it, because a Refit
/// route reads an error body as the expectation's type and drops <c>errors</c>. Otherwise the model
/// the client produced is read, which is what a Kiota client throws. Neither ever throws, because a
/// failure message is being written and this only adds to it.
/// </remarks>
internal static class FieldErrors
{
    /// <summary>"Its errors: id (range), title (required)." or null where the answer carried none.</summary>
    public static string? Describe(ClientAnswer answer)
    {
        List<string>? errors;

        try
        {
            errors = FromContent(answer.Content) ?? FromModel(answer.Body);
        }
        catch (Exception)
        {
            return null;
        }

        return errors is { Count: > 0 } ? "Its errors: " + string.Join(", ", errors) + "." : null;
    }

    private static List<string>? FromContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || Member(document.RootElement, "errors")
                    is not { ValueKind: JsonValueKind.Array } errors
            )
            {
                return null;
            }

            var described = new List<string>();

            foreach (var error in errors.EnumerateArray())
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    Add(described, Text(Member(error, "field")), Text(Member(error, "code")));
                }
            }

            return described;
        }
    }

    private static List<string>? FromModel(object? body)
    {
        if (
            body is null or string
            || Property(body, "Errors")?.GetValue(body) is not IEnumerable errors
            || errors is string
        )
        {
            return null;
        }

        var described = new List<string>();

        foreach (var error in errors)
        {
            if (error != null)
            {
                Add(
                    described,
                    Property(error, "Field")?.GetValue(error)?.ToString(),
                    Property(error, "Code")?.GetValue(error)?.ToString()
                );
            }
        }

        return described;
    }

    private static void Add(List<string> described, string? field, string? code)
    {
        if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(code))
        {
            described.Add($"{field} ({code})");
        }
        else if (!string.IsNullOrEmpty(field) || !string.IsNullOrEmpty(code))
        {
            described.Add(string.IsNullOrEmpty(field) ? code! : field);
        }
    }

    private static JsonElement? Member(JsonElement element, string name)
    {
        foreach (var member in element.EnumerateObject())
        {
            if (string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return member.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } text ? text.GetString()
        : element is { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null
        : element?.GetRawText();

    private static PropertyInfo? Property(object instance, string name) =>
        instance
            .GetType()
            .GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            );
}
