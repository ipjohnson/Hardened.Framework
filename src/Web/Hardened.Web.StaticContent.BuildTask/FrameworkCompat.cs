namespace Hardened.Web.StaticContent.BuildTask;

/// <summary>
/// The two file-system APIs the scan needs that net472 does not have.
/// </summary>
/// <remarks>
/// The task targets both runtimes because MSBuild does: .NET for <c>dotnet build</c>, .NET
/// Framework for Visual Studio on Windows. Rather than weaken the scan to the older runtime's
/// vocabulary, the two gaps are filled here and the calling code stays one shape.
/// </remarks>
internal static class FrameworkCompat {

    /// <summary>
    /// <c>Path.GetRelativePath</c>, which arrived in .NET Core 2.1.
    /// </summary>
    public static string GetRelativePath(string relativeTo, string path) {
#if NET8_0_OR_GREATER
        return Path.GetRelativePath(relativeTo, path);
#else
        var from = new Uri(AppendSeparator(Path.GetFullPath(relativeTo)));
        var to = new Uri(Path.GetFullPath(path));

        if (from.Scheme != to.Scheme) {
            return path;
        }

        var relative = Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString());

        return relative.Replace('/', Path.DirectorySeparatorChar);
#endif
    }

    /// <summary>
    /// Where a link finally points, or null when the path is not a link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FileSystemInfo.ResolveLinkTarget</c> arrived in .NET 6 and has no .NET Framework
    /// equivalent short of P/Invoking <c>GetFinalPathNameByHandle</c>. Rather than carry that, the
    /// older runtime reports "not a link" and the containment check falls back to the lexical one.
    /// </para>
    /// <para>
    /// The consequence is worth stating plainly: a link escaping the content root is caught by
    /// <c>dotnet build</c> and by CI, and not by a Visual Studio design-time build on Windows. CI
    /// is the gate that matters here, and it runs on .NET.
    /// </para>
    /// </remarks>
    public static string? ResolveLinkTarget(string fullPath) {
#if NET8_0_OR_GREATER
        var target = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);

        return target == null ? null : Path.GetFullPath(target.FullName);
#else
        return null;
#endif
    }

#if !NET8_0_OR_GREATER
    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
#endif
}
