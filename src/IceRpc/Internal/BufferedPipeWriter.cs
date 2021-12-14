// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements the buffering methods of a PipeWriter using a lazily created Pipe.</summary>
    /// <remarks>This class could derive directly from PipeWriter: it does not use or implement the property and
    /// method of AsyncCompletePipeWriter.</remarks>
    internal abstract class BufferedPipeWriter : AsyncCompletePipeWriter
    {
        public override bool CanGetUnflushedBytes => PipeWriter.CanGetUnflushedBytes;
        public override long UnflushedBytes => PipeWriter.UnflushedBytes;

        internal PipeOptions PipeOptions { get; init; } = PipeOptions.Default;

        private protected PipeReader? PipeReader => _pipe?.Reader;

        private PipeWriter PipeWriter
        {
            get
            {
                // TODO: would be nice to use a pipe pool.
                _pipe ??= new Pipe(PipeOptions);
                return _pipe.Writer;
            }
        }

        private Pipe? _pipe;

        public override void Advance(int bytes) => PipeWriter.Advance(bytes);

        public override void Complete(Exception? exception = default)
        {
            if (_pipe is Pipe pipe)
            {
                pipe.Writer.Complete();
                pipe.Reader.Complete();
                _pipe = null;
            }
        }

        private protected async ValueTask FlushWriterAsync()
        {
            if (_pipe is Pipe pipe)
            {
                // We're flushing our own internal pipe here.
                _ = await pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        public override Memory<byte> GetMemory(int sizeHint) => PipeWriter.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint) => PipeWriter.GetSpan(sizeHint);
    }
}
