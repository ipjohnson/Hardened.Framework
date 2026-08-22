using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Shared;

/// <summary>
/// The wire vocabulary a code-first enum resolves to.
/// </summary>
/// <remarks>
/// Held directly rather than through a generated document, because this is the definition of a
/// contract rather than a formatting detail: the JSON converter, the parameter binder and the
/// <c>enum</c> array in the published description all read it, and changing what it produces
/// changes what every existing client has to send.
///
/// <para>
/// The single-word cases are first because they are the ones that were wrong. CamelCase stepped
/// back past a leading capital that began no acronym run, so <c>Standard</c> returned
/// <c>Standard</c> - the policy was a no-op for nearly every name there is, and the generated
/// output looked plausible enough that only a character-level assertion would have caught it.
/// </para>
/// </remarks>
public class EnumWireNamingTests {

    [Theory]
    [InlineData("Low", "low")]
    [InlineData("Standard", "standard")]
    [InlineData("Open", "open")]
    [InlineData("InProgress", "inProgress")]
    [InlineData("ScienceFiction", "scienceFiction")]
    public void Value_CamelCasesASingleWordAndAPhrase(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "CamelCase"));

    /// <summary>
    /// An acronym is one word. The run ends at the last capital before a lower-case letter, so the
    /// letter that starts the next word is not swallowed into it.
    /// </summary>
    [Theory]
    [InlineData("IOStream", "ioStream")]
    [InlineData("HTTPProxy", "httpProxy")]
    [InlineData("HTTP", "http")]
    [InlineData("XMLHTTPRequest", "xmlhttpRequest")]
    public void Value_TreatsAnAcronymRunAsOneWord(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "CamelCase"));

    [Theory]
    [InlineData("Low", "low")]
    [InlineData("InProgress", "in-progress")]
    [InlineData("HTTPProxy", "http-proxy")]
    [InlineData("Plus1", "plus1")]
    public void Value_KebabCasesOnWordBoundaries(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "KebabCaseLower"));

    [Theory]
    [InlineData("InProgress", "in_progress")]
    [InlineData("HTTPProxy", "http_proxy")]
    public void Value_SnakeCasesLower(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "SnakeCaseLower"));

    [Theory]
    [InlineData("InProgress", "IN_PROGRESS")]
    [InlineData("Low", "LOW")]
    public void Value_SnakeCasesUpper(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "SnakeCaseUpper"));

    /// <summary>
    /// The opt-out, and it has to be exact: an application sets it because the member names are
    /// already its wire values.
    /// </summary>
    [Theory]
    [InlineData("InProgress")]
    [InlineData("AB12")]
    [InlineData("HTTPProxy")]
    public void Value_LeavesMemberNameUntouched(string member) =>
        Assert.Equal(member, EnumWireNaming.Value(member, "MemberName"));

    /// <summary>
    /// A name already lower-case, or empty, is returned rather than mangled.
    /// </summary>
    [Theory]
    [InlineData("low", "low")]
    [InlineData("", "")]
    public void Value_LeavesAnAlreadyLowerNameAlone(string member, string expected) =>
        Assert.Equal(expected, EnumWireNaming.Value(member, "CamelCase"));

    /// <summary>
    /// An unrecognised naming falls back to the member name rather than throwing. The value comes
    /// from an attribute argument, and a consumer compiled against a newer Hardened can name one
    /// this generator does not know.
    /// </summary>
    [Fact]
    public void Value_FallsBackToTheMemberNameForAnUnknownNaming() =>
        Assert.Equal("InProgress", EnumWireNaming.Value("InProgress", "SomethingElse"));

    /// <summary>
    /// CamelCase, and asserted here rather than only in prose: it is what every application that
    /// says nothing gets, so changing it is a wire break for all of them at once.
    /// </summary>
    [Fact]
    public void DefaultNaming_IsCamelCase() =>
        Assert.Equal("CamelCase", EnumWireNaming.DefaultNaming);
}
