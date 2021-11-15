// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    internal static class SlicFrameReaderExtensions
    {
        internal static async ValueTask<(uint, InitializeBody?)> ReadInitializeAsync(
            this ISlicFrameReader reader,
            FrameType type,
            int dataSize,
            CancellationToken cancel)
        {
            if (type != FrameType.Initialize)
            {
                throw new InvalidDataException(
                    $"unexpected Slic frame type {type}, expected {FrameType.Initialize}");
            }

            using IMemoryOwner<byte> owner = await ReadFrameDataAsync(reader, dataSize, cancel).ConfigureAwait(false);
            var decoder = new Ice20Decoder(owner.Memory[..dataSize]);
            uint version = decoder.DecodeVarUInt();
            if (version == SlicDefinitions.V1)
            {
                return (version, new InitializeBody(decoder));
            }
            else
            {
                return (version, null);
            }
        }

        internal static async ValueTask<(InitializeAckBody?, VersionBody?)> ReadInitializeAckOrVersionAsync(
            this ISlicFrameReader reader,
            FrameType type,
            int dataSize,
            CancellationToken cancel)
        {
            using IMemoryOwner<byte> owner = await ReadFrameDataAsync(reader, dataSize, cancel).ConfigureAwait(false);
            Memory<byte> buffer = owner.Memory[..dataSize];
            return type switch
            {
                FrameType.InitializeAck => (new InitializeAckBody(new Ice20Decoder(buffer)), null),
                FrameType.Version => (null, new VersionBody(new Ice20Decoder(buffer))),
                _ => throw new InvalidDataException($"unexpected Slic frame '{type}'")
            };
        }

        internal static ValueTask<StreamResetBody> ReadStreamResetAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, decoder => new StreamResetBody(decoder), cancel);

        internal static ValueTask<StreamConsumedBody> ReadStreamConsumedAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, decoder => new StreamConsumedBody(decoder), cancel);

        internal static ValueTask<StreamStopSendingBody> ReadStreamStopSendingAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, decoder => new StreamStopSendingBody(decoder), cancel);

        internal static async ValueTask SkipStreamDataAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            (await ReadFrameDataAsync(reader, dataSize, cancel).ConfigureAwait(false)).Dispose();

        private static async ValueTask<IMemoryOwner<byte>> ReadFrameDataAsync(
            ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel)
        {
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(dataSize);
            try
            {
                Memory<byte> buffer = owner.Memory[0..dataSize];
                await reader.ReadFrameDataAsync(buffer, cancel).ConfigureAwait(false);
                return owner;
            }
            catch
            {
                owner.Dispose();
                throw;
            }
        }

        private static async ValueTask<T> ReadFrameDataAsync<T>(
            ISlicFrameReader reader,
            int size,
            Func<Ice20Decoder, T> decodeFunc,
            CancellationToken cancel)
        {
            using IMemoryOwner<byte> data = await ReadFrameDataAsync(reader, size, cancel).ConfigureAwait(false);
            return decodeFunc(new Ice20Decoder(data.Memory[0..size]));
        }
    }
}
