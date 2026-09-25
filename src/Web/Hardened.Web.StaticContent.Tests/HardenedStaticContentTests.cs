using Xunit;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// The module is keyed on <see cref="HardenedStaticContent.Path"/>: two installs over one directory
/// collapse into one, and two over different directories both load.
/// </summary>
public class HardenedStaticContentTests
{
    [Fact]
    public void TwoInstallsOverOneDirectoryCollapseIntoOne()
    {
        Assert.Equal(new HardenedStaticContent(), new HardenedStaticContent());
        Assert.Equal(
            new HardenedStaticContent().GetHashCode(),
            new HardenedStaticContent().GetHashCode()
        );

        Assert.Equal(
            new HardenedStaticContent { Path = null },
            new HardenedStaticContent { Path = null }
        );
        Assert.Equal(
            new HardenedStaticContent { Path = null }.GetHashCode(),
            new HardenedStaticContent { Path = null }.GetHashCode()
        );
    }

    [Fact]
    public void TwoInstallsOverDifferentDirectoriesBothLoad()
    {
        Assert.NotEqual(new HardenedStaticContent(), new HardenedStaticContent { Path = "assets" });
    }
}
