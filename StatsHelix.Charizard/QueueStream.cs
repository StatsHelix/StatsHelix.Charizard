using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public class QueueStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        BlockingCollection<ArraySegment<byte>> ToRead = new BlockingCollection<ArraySegment<byte>>(new ConcurrentQueue<ArraySegment<byte>>());

        bool NoCopy = false;

        ArraySegment<byte>? CurrentlyReading = null;

        /// <summary>
        /// Creates a new QueueStream
        /// </summary>
        /// <param name="noCopy">If true, the contents of the byte-arrays in Write() aren't copied, but the arrays are referenced directly. You guarantee that they are 100% immutable then.</param>
        public QueueStream(bool noCopy)
        {
            NoCopy = noCopy;
        }

        public override void Flush()
        {
            // nop.
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (CurrentlyReading == null)
            {
                try
                {
                    CurrentlyReading = ToRead.Take();
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }
            }

            var currentlyReading = CurrentlyReading.Value;

            var read = Math.Min(currentlyReading.Count, count);

            Array.Copy(currentlyReading.Array, currentlyReading.Offset, buffer, offset, read);

            var left = currentlyReading.Count - read;

            if (left > 0)
                CurrentlyReading = new ArraySegment<byte>(currentlyReading.Array, currentlyReading.Offset + read, left);
            else
                CurrentlyReading = null;

            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (NoCopy)
            {
                ToRead.Add(new ArraySegment<byte>(buffer, offset, count));
            }
            else
            {
                var toEnqueue = new byte[count];
                Array.Copy(toEnqueue, offset, toEnqueue, 0, count);
                ToRead.Add(new ArraySegment<byte>(toEnqueue));
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            ToRead.CompleteAdding();
            base.Close();
        }

    }
}
