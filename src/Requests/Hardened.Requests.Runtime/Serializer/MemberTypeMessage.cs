using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// Puts the member's own type in the serializer's sentence about a value it could not convert.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json names the type of the property it was reading. A value bound through a
/// constructor parameter is read with no property, so the sentence names the type being built:
/// <c>"spentOn":"2026-13-45"</c> against <c>record Expense(DateOnly SpentOn)</c> reads
/// "could not be converted to Ledger.Expense". Records are the template's model shape, and
/// <c>ExceptionToModelConverter</c> could only tell the caller the value was not of the type the
/// field takes.
/// </para>
/// <para>
/// The path still names the member, so the member's type is found by walking the path through the
/// body's metadata. A path the metadata does not resolve leaves the exception as it was.
/// </para>
/// </remarks>
internal static class MemberTypeMessage
{
    private const string Prefix = "The JSON value could not be converted to ";

    public static JsonException Rewrite(JsonException exception, JsonTypeInfo body)
    {
        if (!exception.Message.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return exception;
        }

        Type? member;

        // A resolver that cannot describe a type on the way down throws, and this runs while a 400
        // is being answered.
        try
        {
            member = MemberType(body, exception.Path);
        }
        catch (Exception exp) when (exp is NotSupportedException or InvalidOperationException)
        {
            return exception;
        }

        if (member is null)
        {
            return exception;
        }

        var type = Nullable.GetUnderlyingType(member) ?? member;

        return new JsonException(
            Prefix + (type.FullName ?? type.Name) + ".",
            exception.Path,
            exception.LineNumber,
            exception.BytePositionInLine,
            exception.InnerException
        );
    }

    /// <summary>
    /// The declared type at <paramref name="path"/>, such as <c>$.lines[0].spentOn</c>, or null.
    /// </summary>
    private static Type? MemberType(JsonTypeInfo body, string? path)
    {
        if (string.IsNullOrEmpty(path) || path![0] != '$')
        {
            return null;
        }

        var current = body;
        var position = 1;

        while (position < path.Length)
        {
            string? name;

            if (path[position] == '.')
            {
                var end = path.IndexOfAny(['.', '['], position + 1);

                end = end == -1 ? path.Length : end;
                name = path.Substring(position + 1, end - position - 1);
                position = end;
            }
            else if (
                path[position] == '['
                && position + 1 < path.Length
                && path[position + 1] == '\''
            )
            {
                var end = path.IndexOf("']", position + 2, StringComparison.Ordinal);

                if (end == -1)
                {
                    return null;
                }

                name = path.Substring(position + 2, end - position - 2);
                position = end + 2;
            }
            else if (path[position] == '[')
            {
                var end = path.IndexOf(']', position);

                if (end == -1)
                {
                    return null;
                }

                name = null;
                position = end + 1;
            }
            else
            {
                return null;
            }

            var next = Step(current, name);

            if (next is null)
            {
                return null;
            }

            if (position >= path.Length)
            {
                return next;
            }

            current = current.Options.GetTypeInfo(next);
        }

        return null;
    }

    /// <summary>
    /// The type one step below <paramref name="parent"/>: a property by name, or an element.
    /// </summary>
    private static Type? Step(JsonTypeInfo parent, string? name)
    {
        switch (parent.Kind)
        {
            case JsonTypeInfoKind.Enumerable:
                return name is null ? Element(parent.Type) : null;
            case JsonTypeInfoKind.Dictionary:
                return Element(parent.Type);
            case JsonTypeInfoKind.Object when name is not null:
                return (
                    parent.Properties.FirstOrDefault(p => p.Name == name)
                    ?? parent.Properties.FirstOrDefault(p =>
                        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                    )
                )?.PropertyType;
            default:
                return null;
        }
    }

    /// <summary>
    /// An array's element, or a generic collection's last type argument, which is the element of a
    /// list and the value of a dictionary.
    /// </summary>
    /// <remarks>
    /// <c>JsonTypeInfo.ElementType</c> is public from .NET 9 only, and walking the interfaces is a
    /// trim warning in an assembly marked AOT-compatible. A collection that is not generic itself,
    /// such as a class deriving <c>List&lt;T&gt;</c>, resolves to nothing and keeps the
    /// serializer's message.
    /// </remarks>
    private static Type? Element(Type collection)
    {
        if (collection.IsArray)
        {
            return collection.GetElementType();
        }

        if (!collection.IsGenericType)
        {
            return null;
        }

        var arguments = collection.GetGenericArguments();

        return arguments[arguments.Length - 1];
    }
}
