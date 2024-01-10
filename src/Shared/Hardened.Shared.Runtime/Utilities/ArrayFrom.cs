namespace Hardened.Shared.Runtime.Utilities;

public class ArrayFrom {
    public static object[] ObjectValues(params object[] values) {
        return values;
    }

    public static T[] Values<T>(params T[] values) {
        return values;
    }
}