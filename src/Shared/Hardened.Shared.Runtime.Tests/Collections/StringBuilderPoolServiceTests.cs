using System.Text;
using Hardened.Shared.Runtime.Collections;
using SimpleFixture.xUnit;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Collections;

public class StringBuilderPoolServiceTests
{
    [Theory]
    [AutoData]
    public void GetSameStringBuilder(StringBuilderPool poolService)
    {
        StringBuilder builder;

        using (var builderHolder = poolService.Get())
        {
            builder = builderHolder.Item;
        }

        using (var secondBuilderHolder = poolService.Get())
        {
            Assert.Same(builder, secondBuilderHolder.Item);
        }
    }

    [Theory]
    [AutoData]
    public void GetMultipleBuilders(StringBuilderPool poolService)
    {
        using var builderHolder = poolService.Get();
        using var builderHolder2 = poolService.Get();

        Assert.NotSame(builderHolder, builderHolder2);
    }

    [Theory]
    [AutoData]
    public void ClearStringBuilderUponReturn(StringBuilderPool poolService)
    {
        StringBuilder builder;

        using (var builderHolder = poolService.Get())
        {
            builder = builderHolder.Item;

            builder.Append("Some String");

            Assert.True(builder.Length > 0);
        }

        Assert.Equal(0, builder.Length);
    }
}