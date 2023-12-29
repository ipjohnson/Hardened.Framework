using System.Reflection;

namespace Hardened.Shared.Testing.Utilties;

public static class TypeExtensions {
    public static MethodInfo? FindInstanceMethod(this Type type, string methodName) {
        return type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public) ??
               type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}