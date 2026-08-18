// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// Present on net8.0 and not on net472, where the compiler still requires it to emit an init-only
/// setter. Compiled in only for the older framework.
/// </summary>
internal static class IsExternalInit;
