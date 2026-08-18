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
    /// A stream that decompresses <paramref name="source"/>, or null when this runtime cannot.
    /// </summary>
    /// <remarks>
    /// <c>BrotliStream</c> arrived in .NET Core 2.1 and has no .NET Framework equivalent. A Brotli
    /// sibling therefore cannot be folded into its resource under Visual Studio's MSBuild - the
    /// caller reports that rather than emitting the raw stream under the wrong name. Gzip works on
    /// both, and CI, which is the gate that matters, runs on .NET.
    /// </remarks>
    public static Stream? Decompressor(Stream source, string suffix) {
        if (suffix == ".gz") {
            return new System.IO.Compression.GZipStream(
                source, System.IO.Compression.CompressionMode.Decompress);
        }

#if NET8_0_OR_GREATER
        return new System.IO.Compression.BrotliStream(
            source, System.IO.Compression.CompressionMode.Decompress);
#else
        return null;
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
    /// <b>This is a build-time check only, and the runtime is unaffected.</b>
    /// <c>Hardened.Web.StaticContent</c> targets net8.0 and
    /// <c>FileSystemContentSource.Servable</c> resolves links on every request regardless of what
    /// built the project - this assembly ships in <c>tasks/</c>, never in <c>lib/</c>, and no
    /// application loads it.
    /// </para>
    /// <para>
    /// The one place it matters: a manifest generated under .NET Framework MSBuild has not had its
    /// links resolved, and <c>ManifestContentSource</c> trusts the manifest rather than re-checking.
    /// A manifest is generated into <c>obj/</c> and never committed, so CI regenerates it on .NET
    /// and HSTATIC002 fires there - which makes this a question of how early a developer finds out,
    /// not of what ships.
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
