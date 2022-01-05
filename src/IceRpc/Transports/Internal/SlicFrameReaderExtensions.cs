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
            return Decode(owner.Memory[..dataSize]);

            static (uint, InitializeBody?) Decode(ReadOnlyMemory<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                uint version = decoder.DecodeVarUInt();
                if (version == SlicDefinitions.V1)
                {
                    return (version, new InitializeBody(ref decoder));
                }
                else
                {
                    return (version, null);
                }
            }
        }

        internal static async ValueTask<(InitializeAckBody?, VersionBody?)> ReadInitializeAckOrVersionAsync(
            this ISlicFrameReader reader,
            FrameType type,
            int dataSize,
            CancellationToken cancel)
        {
            using IMemoryOwner<byte> owner = await ReadFrameDataAsync(reader, dataSize, cancel).ConfigureAwait(false);
            return Decode(owner.Memory[..dataSize], type);

            static (InitializeAckBody?, VersionBody?) Decode(ReadOnlyMemory<byte> buffer, FrameType type)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                return type switch
                {
                    FrameType.InitializeAck => (new InitializeAckBody(ref decoder), null),
                    FrameType.Version => (null, new VersionBody(ref decoder)),
                    _ => throw new InvalidDataException($"unexpected Slic frame '{type}'")
                };
            }
        }

        internal static ValueTask<StreamResetBody> ReadStreamResetAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, (ref IceDecoder decoder) => new StreamResetBody(ref decoder), cancel);

        internal static ValueTask<StreamConsumedBody> ReadStreamConsumedAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, (ref IceDecoder decoder) => new StreamConsumedBody(ref decoder), cancel);

        internal static ValueTask<StreamStopSendingBody> ReadStreamStopSendingAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadFrameDataAsync(reader, dataSize, (ref IceDecoder decoder) => new StreamStopSendingBody(ref decoder), cancel);

        internal static ValueTask ReadUnidirectionalStreamReleasedAsync(
            this ISlicFrameReader reader,
            CancellationToken cancel) =>
           reader.ReadFrameDataAsync(Memory<byte>.Empty, cancel);

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
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel)
        {
            using IMemoryOwner<byte> data = await ReadFrameDataAsync(reader, size, cancel).ConfigureAwait(false);
            return Decode(data.Memory[0..size], decodeFunc);

            static T Decode(ReadOnlyMemory<byte> buffer, DecodeFunc<T> decodeFunc)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                return decodeFunc(ref decoder);
            }
        }
    }
}
