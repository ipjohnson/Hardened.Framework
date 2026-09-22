using System.Buffers;
using Hardened.Requests.Abstract.Forms;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// The form one request carried, read once and kept for the rest of the request.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, because a body can be read once. A filter and the handler both asking for the form, or
/// a handler reading it itself after its parameters were bound, would otherwise get an empty form
/// the second time on a body that cannot be rewound.
/// </para>
/// <para>
/// It also owns the buffer a multipart body was read into, which the files are slices of, and
/// returns it to the pool when the request's scope is disposed.
/// </para>
/// </remarks>
internal sealed class RequestFormCache : IDisposable
{
    private byte[]? _buffer;

    public IFormCollection? Form { get; private set; }

    /// <summary>Keeps <paramref name="form"/>, and the pooled buffer it reads from, if any.</summary>
    public void Keep(IFormCollection form, byte[]? pooledBuffer)
    {
        Form = form;
        _buffer = pooledBuffer;
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        Form = null;
    }
}
