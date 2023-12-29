using System.Text;

namespace Hardened.Shared.Runtime.Collections;

public interface IStringBuilderPool : IItemPool<StringBuilder> { }

public class StringBuilderPool : ItemPool<StringBuilder>, IStringBuilderPool {
    public StringBuilderPool() : this(2) { }

    public StringBuilderPool(int defaultSize)
        : base(() => new StringBuilder(defaultSize), b => b.Clear()) { }
}