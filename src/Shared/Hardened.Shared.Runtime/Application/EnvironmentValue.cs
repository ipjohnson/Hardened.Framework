using System.Globalization;

namespace Hardened.Shared.Runtime.Application;

/// <summary>
/// Converts a value an environment holds to the type its reader asks for.
/// </summary>
/// <remarks>
/// <para>
/// A value that does not convert throws the type of exception
/// <see cref="System.Convert.ChangeType(object, Type, IFormatProvider)"/> threw, with a message
/// that names the variable. The failure surfaces far from the read. A configuration model is built
/// when a handler first resolves it, and the log named the handler and the unreadable text but not
/// the variable that held it.
/// </para>
/// <para>
/// Text is parsed with the invariant culture, as route, query and header values are. A nullable
/// type converts to its underlying type.
/// </para>
/// </remarks>
public static class EnvironmentValue
{
    public static T Convert<T>(string name, object value)
    {
        if (value is T typed)
        {
            return typed;
        }

        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        try
        {
            return (T)System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        catch (FormatException exception)
        {
            throw new FormatException(Unreadable(name, type, exception), exception);
        }
        catch (InvalidCastException exception)
        {
            throw new InvalidCastException(Unreadable(name, type, exception), exception);
        }
        catch (OverflowException exception)
        {
            throw new OverflowException(Unreadable(name, type, exception), exception);
        }
    }

    private static string Unreadable(string name, Type type, Exception exception) =>
        $"The environment variable {name} could not be read as {type.Name}: {exception.Message}";
}
