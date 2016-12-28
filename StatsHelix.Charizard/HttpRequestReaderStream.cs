using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public class HttpRequestReaderStream : Stream
    {
        private readonly Stream Underlying;
        private readonly byte[] ReadBuffer = new byte[8192];
        private static readonly Encoding Encoding = Encoding.ASCII;
        private int BufferPosition = 0;
        private int BufferLength = 0;

        public HttpRequestReaderStream(Stream underlying)
        {
            Underlying = underlying;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }

        // rippoff from referencesources' StreamReader
        public async Task<string> ReadLineAsync()
        {
            if (BufferPosition == BufferLength && ((await ReloadReadBuffer()) == 0))
                return null;

            // Yes, we could use a StringBuilder or even a char[] here.
            // However, it really doesn't matter for our case - we're not going to loop
            // very often here anyways (if ever) so we might as well do it the simple
            // and obvious way.
            var buffer = String.Empty;

            do
            {
                for (int i = BufferPosition; i < BufferLength; i++)
                {
                    byte b = ReadBuffer[i];
                    if (b == '\r' || b == '\n')
                    {
                        // this is the return path:
                        buffer += Encoding.GetString(ReadBuffer, BufferPosition, i - BufferPosition);
                        BufferPosition = i + 1; // skip the linebreak

                        // in case of CRLF, skip the \n as well:
                        if (b == '\r' && (BufferPosition < BufferLength || (await ReloadReadBuffer() > 0)))
                            if (ReadBuffer[BufferPosition] == '\n')
                                BufferPosition++;

                        return buffer;
                    }
                }

                buffer += Encoding.GetString(ReadBuffer, BufferPosition, BufferLength - BufferPosition);
            } while (await ReloadReadBuffer() > 0);

            return buffer;
        }

        private async Task<int> ReloadReadBuffer()
        {
            BufferPosition = 0;
            return BufferLength = await Underlying.ReadAsync(ReadBuffer, 0, ReadBuffer.Length);
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (BufferPosition < BufferLength)
            {
                var readCount = Math.Min(count, BufferLength - BufferPosition);
                Buffer.BlockCopy(ReadBuffer, BufferPosition, buffer, offset, readCount);
                BufferPosition += readCount;
                return Task.FromResult(readCount);
            }
            else
            {
                return Underlying.ReadAsync(buffer, offset, count, cancellationToken);
            }
        }

        private Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token, bool isAPM, AsyncCallback callback, object state)
        {
            var tcs = new TaskCompletionSource<int>(state);
            if (token.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else if (BufferPosition < BufferLength)
            {
                var readCount = Math.Min(count, BufferLength - BufferPosition);
                Buffer.BlockCopy(ReadBuffer, BufferPosition, buffer, offset, readCount);
                tcs.SetResult(readCount);
            }
            else
            {
                Underlying.ReadAsync(buffer, offset, count, token);
            }
            return tcs.Task;
        }

        private class ImmediateAsyncResult : IAsyncResult
        {
            public int Result { get; set; }
            public object AsyncState { get; set; }

            public WaitHandle AsyncWaitHandle { get { return new ManualResetEvent(true); } }
            public bool CompletedSynchronously { get { return true; } }
            public bool IsCompleted { get { return true; } }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (BufferPosition < BufferLength)
            {
                var readCount = Math.Min(count, BufferLength - BufferPosition);
                Buffer.BlockCopy(ReadBuffer, BufferPosition, buffer, offset, readCount);

                var ar = new ImmediateAsyncResult { AsyncState = state, Result = readCount };
                callback?.Invoke(ar);
                return ar;
            }
            else
            {
                return Underlying.BeginRead(buffer, offset, count, callback, state);
            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            var iar = asyncResult as ImmediateAsyncResult;
            if (iar != null)
                return iar.Result;

            return Underlying.EndRead(asyncResult);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
                Underlying.Dispose();
        }


        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }
    }
}
