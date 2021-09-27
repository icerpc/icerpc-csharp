// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Slic;
using System.Buffers;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The Slic frame reader class reads Slic frames received from a buffered receiver.</summary>
    internal class BufferedReceiverSlicFrameReader : ISlicFrameReader
    {
        private readonly BufferedReceiver _bufferedStream;

        public void Dispose() => _bufferedStream.Dispose();

        public async ValueTask<(FrameType, int, IMemoryOwner<byte>)> ReadFrameAsync(CancellationToken cancel)
        {
            (FrameType frameType, int frameSize) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            return (frameType, frameSize, await _bufferedStream.ReceiveAsync(frameSize, cancel).ConfigureAwait(false));
        }

        public async ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel)
        {
            (FrameType frameType, int frameSize) = await ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (frameType < FrameType.Stream)
            {
                throw new InvalidDataException($"unexpected Slic frame '{frameType}'");
            }
            (ulong id, int idLength) = await _bufferedStream.ReceiveVarULongAsync(cancel).ConfigureAwait(false);
            return (frameType, frameSize - idLength, (long)id);
        }

        public ValueTask ReadStreamFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _bufferedStream.ReceiveAsync(buffer, cancel);

        public ValueTask<IMemoryOwner<byte>> ReadStreamFrameDataAsync(int length, CancellationToken cancel) =>
            _bufferedStream.ReceiveAsync(length, cancel);

        internal BufferedReceiverSlicFrameReader(BufferedReceiver bufferedStream) =>
            _bufferedStream = bufferedStream;

        private async ValueTask<(FrameType, int)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            var frameType = (FrameType)await _bufferedStream.ReceiveByteAsync(cancel).ConfigureAwait(false);
            int frameSize = await _bufferedStream.ReceiveSizeAsync(cancel).ConfigureAwait(false);
            return (frameType, frameSize);
        }
    }

    /// <summary>The Slic frame reader class reads Slic frames received them over the single stream
    /// connection.</summary>
    internal sealed class StreamSlicFrameReader : BufferedReceiverSlicFrameReader
    {
        internal StreamSlicFrameReader(ISingleStreamConnection stream) :
            base(new BufferedReceiver(stream.ReceiveAsync, 256))
        {
        }
    }
}
