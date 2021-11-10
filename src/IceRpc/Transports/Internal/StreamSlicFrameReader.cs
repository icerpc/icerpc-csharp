// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc.Transports.Internal
{
    /// <summary>The buffered receiver Slic frame reader class reads Slic frames from a buffered
    /// receiver.</summary>
    internal class BufferedReceiverSlicFrameReader : ISlicFrameReader, IDisposable
    {
        private readonly BufferedReceiver _receiver;

        public void Dispose() => _receiver.Dispose();

        public ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _receiver.ReceiveAsync(buffer, cancel);

        public async ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            var frameType = (FrameType)await _receiver.ReceiveByteAsync(cancel).ConfigureAwait(false);
            int frameSize = await _receiver.ReceiveSizeAsync(cancel).ConfigureAwait(false);

            if (frameType >= FrameType.Stream)
            {
                (ulong id, int idLength) = await _receiver.ReceiveVarULongAsync(cancel).ConfigureAwait(false);
                return (frameType, frameSize - idLength, (long)id);
            }
            else
            {
                return (frameType, frameSize, null);
            }
        }

        internal BufferedReceiverSlicFrameReader(BufferedReceiver receiver) => _receiver = receiver;
    }

    /// <summary>The stream Slic frame reader class reads Slic frames received over an <see
    /// cref="ISimpleStream"/>.</summary>
    internal sealed class StreamSlicFrameReader : BufferedReceiverSlicFrameReader
    {
        internal StreamSlicFrameReader(ISimpleStream stream) :
            base(new BufferedReceiver(stream.ReadAsync, 256))
        {
        }
    }
}
