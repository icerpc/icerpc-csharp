// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame writer class writes Slic frames.</summary>
    internal sealed class SlicFrameWriter : ISlicFrameWriter
    {
        private readonly Func<IReadOnlyList<ReadOnlyMemory<byte>>, CancellationToken, ValueTask> _writeFunc;

        public async ValueTask WriteFrameAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            // A Slic frame must always be sent entirely even if the sending is canceled.
            // TODO: Fix ISimpleNetworkConnection.WriteAsync or write using simple network connection PipeWriter
            ValueTask task = _writeFunc(buffers, CancellationToken.None);
            if (task.IsCompleted || !cancel.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
            }
            else
            {
                await task.AsTask().WaitAsync(cancel).ConfigureAwait(false);
            }
        }

        internal SlicFrameWriter(Func<IReadOnlyList<ReadOnlyMemory<byte>>, CancellationToken, ValueTask> writeFunc) =>
            _writeFunc = writeFunc;
    }
}
