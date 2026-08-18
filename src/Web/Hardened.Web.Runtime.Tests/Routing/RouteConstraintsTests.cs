using System.Globalization;
using Hardened.Web.Runtime.Routing;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// The constraint tests themselves, driven directly.
/// </summary>
/// <remarks>
/// The generator suite drives these through a compiled route table, which proves the emit and the
/// wiring. It does not reach the edges — an empty segment, a hyphen in the wrong place, a date that
/// is well-formed but not a real day — and those are where a character loop goes wrong. Cheaper to
/// pin here than through a generated table, and every branch is reachable from this file.
/// </remarks>
public class RouteConstraintsTests {

    [Theory]
    [InlineData("42", true)]
    [InlineData("-7", true)]
    [InlineData("0", true)]
    [InlineData("2147483648", false)]   // one past int.MaxValue
    [InlineData("4.5", false)]
    [InlineData("1,000", false)]        // a culture-sensitive parse would take the group separator
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IsInt(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsInt(value));

    [Theory]
    [InlineData("9007199254740993", true)]
    [InlineData("-1", true)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IsLong(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsLong(value));

    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    public void IsGuid(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsGuid(value));

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", true)]
    [InlineData("yes", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    public void IsBool(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsBool(value));

    /// <summary>
    /// No thousands separator. <c>NumberStyles.Number</c> allows one, which under the invariant
    /// culture made <c>4,5</c> parse as 45 — so a resource had several URLs for one value.
    /// </summary>
    [Theory]
    [InlineData("4.5", true)]
    [InlineData("-0.25", true)]
    [InlineData("42", true)]
    [InlineData("1,000", false)]
    [InlineData("4,5", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IsDecimal(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsDecimal(value));

    /// <summary>
    /// ISO 8601 only. <c>DateTime.TryParse</c> would take most of these, which is the point of not
    /// using it: a URL is the same string in every locale.
    /// </summary>
    [Theory]
    [InlineData("2026-08-17", true)]
    [InlineData("2026-02-29", false)]    // 2026 is not a leap year
    [InlineData("2026-13-01", false)]
    [InlineData("2026-8-17", false)]     // not zero-padded
    [InlineData("12/06/2026", false)]
    [InlineData("17 August 2026", false)]
    [InlineData("", false)]
    public void IsDate(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsDate(value));

    [Theory]
    [InlineData("2026-08-17T09:30:00Z", true)]
    [InlineData("2026-08-17T09:30:00.1234567Z", true)]
    [InlineData("2026-08-17T09:30:00+02:00", true)]
    [InlineData("2026-08-17T09:30Z", true)]
    [InlineData("2026-08-17", true)]
    [InlineData("2026-08-17 09:30", false)]   // space instead of T
    [InlineData("not-a-time", false)]
    [InlineData("", false)]
    public void IsDateTime(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsDateTime(value));

    [Theory]
    [InlineData("beta", true)]
    [InlineData("Beta", true)]
    [InlineData("beta2", false)]
    [InlineData("be-ta", false)]
    [InlineData("", false)]
    public void IsAlpha(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsAlpha(value));

    [Theory]
    [InlineData("0f9AC3", true)]
    [InlineData("abcdef", true)]
    [InlineData("0123456789", true)]
    [InlineData("0g9", false)]
    [InlineData("0x1f", false)]
    [InlineData("", false)]
    public void IsHex(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsHex(value));

    /// <summary>
    /// A slug is a canonical form, so every way of writing the same words twice has to fail — or the
    /// resource has several URLs, which is the thing a slug exists to avoid.
    /// </summary>
    [Theory]
    [InlineData("my-first-post", true)]
    [InlineData("post", true)]
    [InlineData("2026-recap", true)]
    [InlineData("a-b-c-d", true)]
    [InlineData("-leading", false)]
    [InlineData("trailing-", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("Upper", false)]
    [InlineData("under_score", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    public void IsSlug(string value, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsSlug(value));

    [Theory]
    [InlineData("abc123", 6, true)]
    [InlineData("abc12", 6, false)]
    [InlineData("", 0, true)]
    public void IsLengthExact(string value, int length, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsLength(value, length));

    [Theory]
    [InlineData("abcd", 3, 9, true)]
    [InlineData("ab", 3, 9, false)]
    [InlineData("abcdefghij", 3, 9, false)]
    [InlineData("abc", 3, 3, true)]
    public void IsLengthBounded(string value, int min, int max, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsLength(value, min, max));

    [Theory]
    [InlineData("abcd", 4, true)]
    [InlineData("abc", 4, false)]
    public void IsMinLength(string value, int min, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsMinLength(value, min));

    [Theory]
    [InlineData("abcd", 4, true)]
    [InlineData("abcde", 4, false)]
    public void IsMaxLength(string value, int max, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsMaxLength(value, max));

    /// <summary>
    /// The parse is part of the constraint, so a non-integer fails the bound rather than throwing.
    /// </summary>
    [Theory]
    [InlineData("1", 1, true)]
    [InlineData("0", 1, false)]
    [InlineData("-1", 0, false)]
    [InlineData("abc", 1, false)]
    [InlineData("", 1, false)]
    public void IsMin(string value, long min, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsMin(value, min));

    [Theory]
    [InlineData("10", 10, true)]
    [InlineData("11", 10, false)]
    [InlineData("abc", 10, false)]
    public void IsMax(string value, long max, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsMax(value, max));

    [Theory]
    [InlineData("250", 1, 500, true)]
    [InlineData("1", 1, 500, true)]
    [InlineData("500", 1, 500, true)]
    [InlineData("0", 1, 500, false)]
    [InlineData("501", 1, 500, false)]
    [InlineData("abc", 1, 500, false)]
    public void IsRange(string value, long min, long max, bool expected) =>
        Assert.Equal(expected, RouteConstraints.IsRange(value, min, max));

    /// <summary>
    /// A route is part of a URL, which is the same string in every locale. Parsing under an ambient
    /// culture would make the same request match on one machine and not another.
    /// </summary>
    [Fact]
    public void ParsingDoesNotFollowTheAmbientCulture() {
        var original = CultureInfo.CurrentCulture;

        try {
            // de-DE swaps the decimal point and the group separator.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.False(RouteConstraints.IsInt("1.000"));
            Assert.True(RouteConstraints.IsDecimal("4.5"));
            Assert.False(RouteConstraints.IsDecimal("4,5"));
            Assert.True(RouteConstraints.IsDate("2026-08-17"));
        } finally {
            CultureInfo.CurrentCulture = original;
        }
    }
}
