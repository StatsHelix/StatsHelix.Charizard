using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace StatsHelix.Charizard
{
    public class QueueStream : Stream
    {
        public bool NoCopy { get; }

        public BufferBlock<ArraySegment<byte>> Queue { get; } = new BufferBlock<ArraySegment<byte>>();

        /// <summary>
        /// Creates a new QueueStream
        /// </summary>
        /// <param name="noCopy">If true, the contents of the byte-arrays in Write() aren't copied, but the arrays are referenced directly. You guarantee that they are 100% immutable then.</param>
        public QueueStream(bool noCopy)
        {
            NoCopy = noCopy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArraySegment<byte> segment;
            if (NoCopy)
            {
                segment = new ArraySegment<byte>(buffer, offset, count);
            }
            else
            {
                var toEnqueue = new byte[count];
                Array.Copy(buffer, offset, toEnqueue, 0, count);
                segment = new ArraySegment<byte>(toEnqueue);
            }

            if (!Queue.Post(segment))
                throw new ObjectDisposedException("Presumably you're trying to write to a closed QueueStream.");
        }

        protected override void Dispose(bool disposing)
        {
            Queue.Complete();
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
    }
}
