// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Slic;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The buffered receiver Slic frame reader class reads Slic frames from a buffered
    /// receiver.</summary>
    internal class BufferedReceiverSlicFrameReader : ISlicFrameReader
    {
        private readonly BufferedReceiver _receiver;

        public void Dispose() => _receiver.Dispose();

        public ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _receiver.ReceiveAsync(buffer, cancel);

        public async ValueTask<(FrameType, int)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            var frameType = (FrameType)await _receiver.ReceiveByteAsync(cancel).ConfigureAwait(false);
            int frameSize = await _receiver.ReceiveSizeAsync(cancel).ConfigureAwait(false);
            return (frameType, frameSize);
        }

        public async ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel)
        {
            (FrameType frameType, int frameSize) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (frameType < FrameType.Stream)
            {
                throw new InvalidDataException($"unexpected Slic frame '{frameType}'");
            }
            (ulong id, int idLength) = await _receiver.ReceiveVarULongAsync(cancel).ConfigureAwait(false);
            return (frameType, frameSize - idLength, (long)id);
        }

        internal BufferedReceiverSlicFrameReader(BufferedReceiver receiver) => _receiver = receiver;
    }

    /// <summary>The stream Slic frame reader class reads Slic frames received over an <see
    /// cref="ISingleStreamConnection"/>.</summary>
    internal sealed class StreamSlicFrameReader : BufferedReceiverSlicFrameReader
    {
        internal StreamSlicFrameReader(ISingleStreamConnection stream) :
            base(new BufferedReceiver(stream.ReceiveAsync, 256))
        {
        }
    }
}
