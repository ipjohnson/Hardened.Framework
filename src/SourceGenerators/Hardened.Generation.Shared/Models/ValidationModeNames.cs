namespace Hardened.Generation.Models;

/// <summary>
/// The two spellings of a validation mode: the value a description writes, and the
/// <c>ValidationStopMode</c> member the generated code names.
/// </summary>
/// <remarks>
/// The member name is what the model carries, because it is what the spec bridge emits and what
/// the document writer reads back off a code-first declaration. The written form is kebab-case,
/// which is how the rest of a description spells a value.
/// </remarks>
internal static class ValidationModeNames
{
    public const string StopOnFirstError = "StopOnFirstError";

    public const string CollectAll = "CollectAll";

    /// <summary>What a description may write, for a message refusing anything else.</summary>
    public const string Accepted = "stop-on-first-error or collect-all";

    /// <summary>The member a written value names, or null for one that names none.</summary>
    public static string? FromWritten(string? written) =>
        written switch
        {
            "stop-on-first-error" => StopOnFirstError,
            "collect-all" => CollectAll,
            _ => null,
        };

    /// <summary>The value a description writes for <paramref name="member"/>.</summary>
    public static string ToWritten(string member) =>
        member == StopOnFirstError ? "stop-on-first-error" : "collect-all";
}
