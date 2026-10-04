namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// What a generated enum converter does with a value its type does not declare.
/// </summary>
/// <remarks>
/// <para>
/// Throwing stops the read, so a body with an undeclared enum value reported that value and nothing
/// else, although its other members may also have been invalid. Skipping the value instead is not
/// possible from inside a converter: it cannot see the path it is reading, and the path is the
/// field the caller is told about.
/// </para>
/// <para>
/// So the request deserializer reads the body again. Each read tolerates the undeclared values the
/// reads before it found, and throws at the next one with System.Text.Json's path on it. A read that
/// reaches the end has a body whose other members the constraints can check. Converters run in
/// document order, so the values tolerated are the ones already reported.
/// </para>
/// <para>
/// Per thread, because a converter instance is shared by every request and the deserializer reads
/// a buffered body synchronously. Outside <see cref="Tolerate"/> nothing is tolerated and
/// <see cref="Refuse{T}"/> throws, which is what a client deserializing a response wants.
/// </para>
/// </remarks>
public static class UndeclaredValue
{
    [ThreadStatic]
    private static int _tolerated;

    /// <summary>
    /// <c>default</c> where the current read tolerates another undeclared value, and otherwise
    /// <see cref="UndeclaredValueException"/> carrying <paramref name="message"/>.
    /// </summary>
    public static T Refuse<T>(string message)
    {
        if (_tolerated > 0)
        {
            _tolerated--;

            return default!;
        }

        throw new UndeclaredValueException(message);
    }

    /// <summary>
    /// Tolerates the first <paramref name="count"/> undeclared values read on this thread until the
    /// scope is disposed.
    /// </summary>
    public static Scope Tolerate(int count)
    {
        _tolerated = count;

        return default;
    }

    /// <summary>Ends a <see cref="Tolerate"/>.</summary>
    public readonly struct Scope : IDisposable
    {
        public void Dispose() => _tolerated = 0;
    }
}
