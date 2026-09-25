namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// How much of a form body the reader will hold.
/// </summary>
/// <remarks>
/// Registered by the request module with its defaults and amended with
/// <c>services.ConfigureForms</c>.
/// </remarks>
public interface IFormConfiguration
{
    /// <summary>
    /// The most bytes a form body may carry. A <c>multipart/form-data</c> or
    /// <c>application/x-www-form-urlencoded</c> body longer than this answers 413.
    /// </summary>
    long MaxBodyBytes { get; }
}
