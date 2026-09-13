using System.Text;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Shared.Runtime.Collections;

public interface IStringBuilderPool : IItemPool<StringBuilder>;

[SingletonService(Using = RegistrationType.Try)]
public class StringBuilderPool : ItemPool<StringBuilder>, IStringBuilderPool
{
    public StringBuilderPool()
        : this(2) { }

    public StringBuilderPool(int defaultSize)
        : base(() => new StringBuilder(defaultSize), b => b.Clear()) { }
}
