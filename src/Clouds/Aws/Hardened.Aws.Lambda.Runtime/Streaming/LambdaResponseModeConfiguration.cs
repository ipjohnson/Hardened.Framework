using Hardened.Shared.Runtime.Application;

namespace Hardened.Aws.Lambda.Runtime.Streaming;

/// <summary>
/// The response mode the function was deployed with. See <see cref="LambdaResponseMode"/>.
/// </summary>
public interface ILambdaResponseModeConfiguration
{
    LambdaResponseMode Mode { get; }
}

/// <summary>
/// Read once at startup from <see cref="EnvironmentVariable"/>, and open to amendment through the
/// application's configuration:
/// </summary>
/// <code>
/// config.Amend((LambdaResponseModeConfiguration mode) => mode.Mode = LambdaResponseMode.Stream);
/// </code>
/// <remarks>
/// An unrecognised value fails the application at startup rather than falling back to buffered. A
/// deployment that spelt the variable wrong would otherwise run buffered behind a front door
/// expecting the prelude, and the first request would be a 500 with nothing in the logs to say why.
/// </remarks>
public class LambdaResponseModeConfiguration : ILambdaResponseModeConfiguration
{
    public const string EnvironmentVariable = "HARDENED_LAMBDA_RESPONSE_MODE";

    public const string BufferedValue = "buffered";

    public const string StreamValue = "stream";

    public const string MixedValue = "mixed";

    public LambdaResponseMode Mode { get; set; } = LambdaResponseMode.Buffered;

    /// <summary>
    /// The mode a setting names. Null or empty is buffered, the confirmed default.
    /// </summary>
    public static LambdaResponseMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Matches(value!, BufferedValue))
        {
            return LambdaResponseMode.Buffered;
        }

        if (Matches(value!, StreamValue))
        {
            return LambdaResponseMode.Stream;
        }

        if (Matches(value!, MixedValue))
        {
            return LambdaResponseMode.Mixed;
        }

        throw new InvalidOperationException(
            $"{EnvironmentVariable} is '{value}'. It must be '{BufferedValue}', '{StreamValue}' or "
                + $"'{MixedValue}'. A function URL in RESPONSE_STREAM invoke mode takes 'stream'. A front "
                + "door that takes a streamed or a buffered answer from each invocation takes 'mixed'. "
                + "Every other deployment takes 'buffered'."
        );
    }

    /// <summary>
    /// The value a mode is written as, which is what the CDK puts in the environment.
    /// </summary>
    public static string ValueOf(LambdaResponseMode mode) =>
        mode switch
        {
            LambdaResponseMode.Stream => StreamValue,
            LambdaResponseMode.Mixed => MixedValue,
            _ => BufferedValue,
        };

    public static void FromEnvironment(
        IHardenedEnvironment environment,
        LambdaResponseModeConfiguration configuration
    )
    {
        configuration.Mode = Parse(environment.Value<string>(EnvironmentVariable));
    }

    private static bool Matches(string value, string expected) =>
        string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
