using Hardened.Shared.Runtime.Utilities;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Utilities;

public class FileExtToMimeTypeHelperTests {
    private readonly FileExtToMimeTypeHelper _helper = new();

    [Theory]
    [InlineData("css", "text/css", false)]
    [InlineData("js", "text/javascript", false)]
    [InlineData("json", "application/json", false)]
    [InlineData("html", "text/html", false)]
    [InlineData("png", "image/png", true)]
    [InlineData("jpg", "image/jpeg", true)]
    [InlineData("pdf", "application/pdf", true)]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", true)]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true)]
    [InlineData("zip", "application/zip", true)]
    public void ReturnsCorrectMimeType_ForKnownExtensions(string ext, string expectedMime, bool expectedBinary) {
        var (mimeType, isBinary) = _helper.GetMimeTypeInfo(ext);

        Assert.Equal(expectedMime, mimeType);
        Assert.Equal(expectedBinary, isBinary);
    }

    [Theory]
    [InlineData("css", false)]
    [InlineData("js", false)]
    [InlineData("json", false)]
    [InlineData("html", false)]
    [InlineData("xml", false)]
    [InlineData("png", true)]
    [InlineData("jpg", true)]
    [InlineData("pdf", true)]
    [InlineData("zip", true)]
    [InlineData("gif", true)]
    public void ReturnsCorrectIsBinaryFlag(string ext, bool expectedBinary) {
        var (_, isBinary) = _helper.GetMimeTypeInfo(ext);

        Assert.Equal(expectedBinary, isBinary);
    }

    [Fact]
    public void ReturnsDefault_ForUnknownExtension() {
        var (mimeType, isBinary) = _helper.GetMimeTypeInfo("xyz");

        Assert.Equal("application/bin", mimeType);
        Assert.True(isBinary);
    }

    [Theory]
    [InlineData(".css", "text/css")]
    [InlineData("css", "text/css")]
    [InlineData(".json", "application/json")]
    [InlineData("json", "application/json")]
    public void HandlesExtensions_WithAndWithoutLeadingDot(string ext, string expectedMime) {
        var (mimeType, _) = _helper.GetMimeTypeInfo(ext);

        Assert.Equal(expectedMime, mimeType);
    }

    [Theory]
    [InlineData("CSS")]
    [InlineData("Css")]
    [InlineData("cSs")]
    public void IsCaseInsensitive(string ext) {
        var (mimeType, _) = _helper.GetMimeTypeInfo(ext);

        Assert.Equal("text/css", mimeType);
    }
}
