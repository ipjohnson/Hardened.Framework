namespace Hardened.Shared.Runtime.Application;

public class EnvironmentImpl : IHardenedEnvironment
{
    private readonly IDictionary<string, string>? _environmentValues;
    private readonly IDictionary<string, object>? _customData;

    public EnvironmentImpl(
        string? name = null,
        IDictionary<string, string>? environmentValues = null,
        IReadOnlyList<string>? arguments = null,
        IDictionary<string, object>? customData = null
    )
    {
        Name =
            name
            ?? System.Environment.GetEnvironmentVariable("HARDENED_ENVIRONMENT")
            ?? DefaultName(System.Environment.GetEnvironmentVariable);
        _environmentValues = environmentValues;
        _customData = customData;
        Arguments = arguments ?? Array.Empty<string>();
    }

    public string Name { get; }

    /// <summary>
    /// The name an environment takes when neither the constructor nor <c>HARDENED_ENVIRONMENT</c>
    /// gives one: <c>production</c> where the AWS Lambda service started the process, and
    /// <c>development</c> anywhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deployed function whose configuration left out <c>HARDENED_ENVIRONMENT</c> ran as
    /// <c>development</c>, and served <c>/docs</c> and anything else gated on it, with nothing at
    /// startup to say so. The Lambda service sets <c>AWS_LAMBDA_RUNTIME_API</c> before the process
    /// starts. The template's <c>Program.cs</c> builds its environment before
    /// <c>LambdaEmulator.StartIfLocal</c> sets the same variable for a local run, so a function run
    /// from an IDE or <c>dotnet run</c> is still <c>development</c>.
    /// </para>
    /// </remarks>
    /// <param name="variable">Reads an environment variable, or answers null when it is unset.</param>
    public static string DefaultName(Func<string, string?> variable) =>
        string.IsNullOrEmpty(variable("AWS_LAMBDA_RUNTIME_API")) ? "development" : "production";

    public IReadOnlyList<string> Arguments { get; }

    public T? Value<T>(string name, T? defaultValue = default)
    {
        string? envValue = null;

        _environmentValues?.TryGetValue(name, out envValue);

        if (string.IsNullOrEmpty(envValue))
        {
            envValue = Environment.GetEnvironmentVariable(name);
        }

        if (!string.IsNullOrEmpty(envValue))
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)envValue;
            }

            return (T)Convert.ChangeType(envValue, typeof(T));
        }

        return defaultValue;
    }

    public T? CustomData<T>(string name, T? defaultValue = default)
    {
        if (_customData != null && _customData.TryGetValue(name, out var value))
        {
            return (T)value;
        }

        return defaultValue;
    }
}
