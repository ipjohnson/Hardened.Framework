using Xunit;
using Xunit.Sdk;

namespace Hardened.Web.Testing.Tests;

/// <summary>
/// These assertions are what consumers of Hardened.Web.Testing rely on to decide whether
/// their own tests pass, so each one is checked in both directions: it must accept the
/// status codes it documents and reject the ones it does not.
/// </summary>
public class WebAssertThatTests {

    private static IWebAssertThat AssertFor(int status) =>
        new TestWebResponse(new FakeExecutionResponse(status)).Assert;

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(299)]
    public void OkAcceptsSuccessRange(int status) {
        AssertFor(status).Ok();
    }

    [Theory]
    [InlineData(199)]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void OkRejectsOutsideSuccessRange(int status) {
        Assert.ThrowsAny<XunitException>(() => AssertFor(status).Ok());
    }

    [Fact]
    public void NotFoundAccepts404() {
        AssertFor(404).NotFound();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(500)]
    public void NotFoundRejectsOtherStatuses(int status) {
        Assert.ThrowsAny<XunitException>(() => AssertFor(status).NotFound());
    }

    [Fact]
    public void BadRequestAccepts400() {
        AssertFor(400).BadRequest();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(404)]
    public void BadRequestRejectsOtherStatuses(int status) {
        Assert.ThrowsAny<XunitException>(() => AssertFor(status).BadRequest());
    }

    [Fact]
    public void UnauthorizedAccepts401() {
        AssertFor(401).Unauthorized();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(403)]
    public void UnauthorizedRejectsOtherStatuses(int status) {
        Assert.ThrowsAny<XunitException>(() => AssertFor(status).Unauthorized());
    }

    [Fact]
    public void ForbiddenAccepts403() {
        AssertFor(403).Forbidden();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(404)]
    public void ForbiddenRejectsOtherStatuses(int status) {
        Assert.ThrowsAny<XunitException>(() => AssertFor(status).Forbidden());
    }

    /// <summary>
    /// Unauthorized (401) and Forbidden (403) are easy to transpose. If they were swapped
    /// the individual assertions above would still pass, so pin the pairing explicitly.
    /// </summary>
    [Fact]
    public void UnauthorizedAndForbiddenAreNotInterchangeable() {
        Assert.ThrowsAny<XunitException>(() => AssertFor(401).Forbidden());
        Assert.ThrowsAny<XunitException>(() => AssertFor(403).Unauthorized());
    }
}
