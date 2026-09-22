using System.Text.Json.Serialization;
using ValidationModules.Constraints;

namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>
/// The eight fields RequestBench's <c>forms.urlencoded</c> test posts, bound as one model.
/// </summary>
public record SearchForm(
    int Page,
    int Size,
    string Status,
    string Category,
    string Sort,
    string Q,
    int MinPrice,
    int MaxPrice
);

/// <summary>
/// A form model written as a class rather than a positional record.
/// </summary>
/// <remarks>
/// One member of each kind the binder treats differently: renamed and set in the initializer,
/// constrained, defaulted by an initializer, and a collection that may be absent.
/// </remarks>
public class ProfileForm
{
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [Range(18, 120)]
    public int Age { get; set; }

    public string Theme { get; set; } = "light";

    public List<string>? Interests { get; set; }
}
