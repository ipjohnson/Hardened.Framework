namespace System.Runtime.CompilerServices;

/// <summary>
/// The marker the compiler needs to emit an <c>init</c> accessor.
/// </summary>
/// <remarks>
/// Declared here for the .NET Framework target, which predates it. The records compiled in from
/// ValidationModules use <c>init</c>, and MSBuild on Visual Studio runs the .NET Framework flavour
/// of this task - so without it the task builds for net8.0 and fails for net472, which is the
/// half nobody notices until someone opens the solution in VS.
///
/// Conditioned in the project file rather than by #if, because on net8.0 the type exists and
/// declaring a second one is an error rather than a duplicate.
/// </remarks>
internal static class IsExternalInit { }
