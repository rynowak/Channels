using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Channels.Samples.Text
{
    public abstract class TextChannelWriter
    {
        private readonly int _chunkSizeInBytes;
        private readonly int _chunkSizeInChars;
        private readonly Encoder _encoder;

        protected TextChannelWriter(IWritableChannel channel, Encoding encoding, int chunkSizeInChars)
        {
            Channel = channel;
            Encoding = encoding;

            _encoder = encoding.GetEncoder();
            _chunkSizeInBytes = encoding.GetMaxByteCount(chunkSizeInChars);

            Chars = new char[chunkSizeInChars];
        }

        protected IWritableChannel Channel { get; }

        protected char[] Chars { get; }

        public Encoding Encoding { get; }

        protected Task WriteChunkAsync(int length, bool flush)
        {
            WritableBuffer buffer;
            if (_encoder.FallbackBuffer.Remaining > 0)
            {
                buffer = Channel.Alloc(_chunkSizeInBytes);
            }
            else
            {
                buffer = Channel.Alloc(Encoding.GetMaxByteCount(_chunkSizeInChars + _encoder.FallbackBuffer.Remaining));
            }

            int charsUsed;
            int bytesUsed;
            bool completed;

            _encoder.Convert(
                Chars,
                0,
                _chunkSizeInChars,
                buffer.Memory.Array,
                buffer.Memory.Offset,
                buffer.Memory.Length,
                flush,
                out charsUsed,
                out bytesUsed,
                out completed);

            Debug.Assert(completed); // Our buffer should always be big enough.
            Debug.Assert(charsUsed == _chunkSizeInChars);

            // This won't always equal _chunkSizeInBytes due to surrogate pairs.
            buffer.CommitBytes(bytesUsed);

            return buffer.FlushAsync();
        }

        public Task WriteAsync(string text)
        {
            if (text.Length <= _chunkSizeInChars)
            {
                return WriteCoreSingleChunkAsync(text);
            }

            return WriteCoreMultipleChunksAsync(text);
        }

        private Task WriteCoreSingleChunkAsync(string text)
        {
            var count = text.Length;
            text.CopyTo(count, Chars, 0, count);

            return WriteChunkAsync(count, flush: true);
        }

        private async Task WriteCoreMultipleChunksAsync(string text)
        {
            var charIndex = 0;
            var charLength = text.Length;

            // handles chunks other than the last chunk to avoid branching and special cases
            while (charLength - charIndex > _chunkSizeInChars)
            {
                text.CopyTo(charIndex, Chars, 0, _chunkSizeInChars);
                charIndex += _chunkSizeInChars;

                await WriteChunkAsync(_chunkSizeInChars, flush: false);
            }

            // Residue
            {
                var count = charLength - charIndex;
                text.CopyTo(charIndex, Chars, 0, count);

                await WriteChunkAsync(count, flush: true);
            }
        }
    }
}
