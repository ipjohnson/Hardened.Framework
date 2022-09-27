using Hardened.Shared.Runtime.Collections;

namespace Hardened.Shared.Runtime.Utilities
{
    public class MemoryStreamPoolWrapper : Stream
    {
        private readonly IPoolItemReservation<MemoryStream> _poolItemReservation;

        public MemoryStreamPoolWrapper(IPoolItemReservation<MemoryStream> poolItemReservation)
        {
            _poolItemReservation = poolItemReservation;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poolItemReservation.Dispose();
            }
        }

        public override void Flush()
        {
            _poolItemReservation.Item.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _poolItemReservation.Item.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _poolItemReservation.Item.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _poolItemReservation.Item.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _poolItemReservation.Item.Write(buffer, offset, count);
        }

        public override bool CanRead => _poolItemReservation.Item.CanRead;

        public override bool CanSeek => _poolItemReservation.Item.CanSeek;

        public override bool CanWrite => _poolItemReservation.Item.CanWrite;

        public override long Length => _poolItemReservation.Item.Length;

        public override long Position
        {
            get => _poolItemReservation.Item.Position;
            set => _poolItemReservation.Item.Position = value;
        }
    }
}
