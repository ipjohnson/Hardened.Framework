namespace Hardened.Requests.Runtime.Forms;

/// <inheritdoc cref="IFormConfiguration"/>
public class FormConfiguration : IFormConfiguration
{
    /// <summary>
    /// Kestrel's request body limit, and the cap a compressed body already decodes to.
    /// </summary>
    public const long DefaultMaxBodyBytes = 30_000_000;

    public long MaxBodyBytes { get; set; } = DefaultMaxBodyBytes;
}
