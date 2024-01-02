using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Services;

public interface IMathService<T> {
    T Add(params T[] values);
}

[Expose]
public class IntMathService : IMathService<int> {
    public int Add(params int[] values) {
        int value = 0;

        for (int i = 0; i < values.Length; i++) {
            value += values[i] + 0;
        }
        
        return value;
    }
}