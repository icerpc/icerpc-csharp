// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Slic frame writer class writes Slic frames.</summary>
    internal sealed class SlicFrameWriter : ISlicFrameWriter
    {
        private readonly PipeWriter _writer;

        public async ValueTask WriteFrameAsync(
            ReadOnlyMemory<byte> slicHeader,
            ReadOnlySequence<byte> protocolHeader,
            ReadOnlySequence<byte> payload,
            CancellationToken cancel)
        {
            // Copy the Slic header
            slicHeader.CopyTo(_writer.GetMemory(slicHeader.Length));
            _writer.Advance(slicHeader.Length);

            // Copy the protocol header
            foreach (ReadOnlyMemory<byte> memory in protocolHeader)
            {
                memory.CopyTo(_writer.GetMemory(memory.Length));
                _writer.Advance(memory.Length);
            }

            // A Slic packet might always be fully sent so we use a None cancellation token to ensure the write won't
            // be canceled.

            if (_writer is ReadOnlySequencePipeWriter writer)
            {
                await WaitTask(writer.WriteAsync(
                    payload,
                    completeWhenDone: false,
                    CancellationToken.None)).ConfigureAwait(false);
            }
            else
            {
                // If the simple network connection output pipe writer doesn't support a ReadOnlySequence write method.
                // Take the slow path by copying the data to the output pipe writer and flushing it.
                foreach (ReadOnlyMemory<byte> memory in payload)
                {
                    memory.CopyTo(_writer.GetMemory(memory.Length));
                    _writer.Advance(slicHeader.Length);
                }
                await WaitTask(_writer.FlushAsync(CancellationToken.None)).ConfigureAwait(false);
            }

            async ValueTask WaitTask(ValueTask<FlushResult> task)
            {
                if (task.IsCompleted || !cancel.CanBeCanceled)
                {
                    await task.ConfigureAwait(false);
                }
                else
                {
                    await task.AsTask().WaitAsync(cancel).ConfigureAwait(false);
                }
            }
        }

        internal SlicFrameWriter(PipeWriter writer) => _writer = writer;
    }
}
