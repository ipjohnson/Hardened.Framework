namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A percent-encoded path, through the harness, answering what a socket answers.
/// </summary>
/// <remarks>
/// <para>
/// SU-07. <c>app.Get("/binding/path/%20")</c> reached the handler as the literal three characters
/// while the same request over Kestrel decoded to whitespace - so a test could pass here and the
/// application behave differently in production, which is the one thing a harness exists not to
/// do.
/// </para>
/// <para>
/// Every expectation below was measured against the Kestrel integration application over a real
/// socket rather than reasoned about: the separator that stays encoded and the plus that is not a
/// space are both surprises.
/// </para>
/// </remarks>
public class EncodedPathTests {

    private static async Task<string> Token(ITestWebApp app, string encoded) {
        var response = await app.Get("/binding/path/" + encoded);

        response.Assert.Ok();

        return response.Deserialize<string>();
    }

    [HardenedTest]
    public async Task AnEscapedSpaceReachesTheHandlerAsASpace(ITestWebApp app) {
        Assert.Equal(" ", await Token(app, "%20"));
    }

    [HardenedTest]
    public async Task AMultiByteCharacterIsDecodedWhole(ITestWebApp app) {
        Assert.Equal("café", await Token(app, "caf%C3%A9"));
    }

    [HardenedTest]
    public async Task AnEscapedPercentIsAPercent(ITestWebApp app) {
        Assert.Equal("a%b", await Token(app, "a%25b"));
    }

    /// <summary>
    /// The one escape that stays. Decoding it would put a separator inside a segment, changing how
    /// many segments the path has.
    /// </summary>
    [HardenedTest]
    public async Task AnEscapedSeparatorStaysEncoded(ITestWebApp app) {
        Assert.Equal("a%2Fb", await Token(app, "a%2Fb"));
    }

    /// <summary>
    /// In either case. An escape is hex, and hex is case-insensitive - Kestrel decodes
    /// <c>%c3%a9</c> and leaves <c>%2f</c>, exactly as it does the capitals.
    /// </summary>
    [HardenedTest]
    public async Task TheCaseOfAnEscapeDoesNotMatter(ITestWebApp app) {
        Assert.Equal("a%2fb", await Token(app, "a%2fb"));
        Assert.Equal("café", await Token(app, "caf%c3%a9"));
    }

    /// <summary>A plus is a plus in a path. Only a query string reads one as a space.</summary>
    [HardenedTest]
    public async Task APlusIsNotASpace(ITestWebApp app) {
        Assert.Equal("a+b", await Token(app, "a+b"));
    }

    /// <summary>Two characters that are not hex are not an escape.</summary>
    [HardenedTest]
    public async Task SomethingThatIsNotAnEscapeIsLeftAlone(ITestWebApp app) {
        Assert.Equal("a%zz", await Token(app, "a%zz"));
    }

    /// <summary>
    /// And neither is an escape that runs out of path before it has two digits.
    /// </summary>
    /// <summary>
    /// Nor are two characters that sit outside hex on the other side of it.
    /// </summary>
    [HardenedTest]
    public async Task DigitsBelowHexAreNotAnEscapeEither(ITestWebApp app) {
        Assert.Equal("a%-1b", await Token(app, "a%-1b"));
        Assert.Equal("a%G0b", await Token(app, "a%G0b"));
    }

    [HardenedTest]
    public async Task AnEscapeWithNothingAfterItIsLeftAlone(ITestWebApp app) {
        Assert.Equal("a%", await Token(app, "a%"));
        Assert.Equal("a%2", await Token(app, "a%2"));
    }
}
