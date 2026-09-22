using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Runtime.Validation;
using ValidationModules;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// The checks the generated binder makes on a file parameter.
/// </summary>
/// <remarks>
/// A missing required file answers the same 400 a missing required field does, with
/// <c>required</c> naming the field, because to the caller they are the same mistake.
/// </remarks>
public static class FormFileBinding
{
    /// <summary><paramref name="file"/>, or a <c>required</c> failure when it was not sent.</summary>
    public static IFormFile Required(IFormFile? file, string name) => file ?? throw Missing(name);

    /// <summary><paramref name="files"/>, or a <c>required</c> failure when none were sent.</summary>
    public static IReadOnlyList<IFormFile> RequiredMany(
        IReadOnlyList<IFormFile> files,
        string name
    ) => files.Count > 0 ? files : throw Missing(name);

    /// <summary><paramref name="files"/>, or null when none were sent.</summary>
    public static IReadOnlyList<IFormFile>? OptionalMany(IReadOnlyList<IFormFile> files) =>
        files.Count > 0 ? files : null;

    private static Validation.ValidationException Missing(string name) =>
        new(
            ValidationResult.FromErrors(
                new[]
                {
                    new ValidationError(name, ValidationCodes.Required, $"{name} is required."),
                }
            )
        );
}
