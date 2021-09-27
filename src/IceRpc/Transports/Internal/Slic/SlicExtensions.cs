// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports.Slic;
using System.Buffers;

namespace IceRpc.Transports.Internal.Slic
{
    internal static class SlicExtensions
    {
        internal static IEnumerable<(ParameterKey, ulong)> DecodedParameters(
            this IDictionary<int, IList<byte>> parameters) =>
            parameters.Select(pair =>
                ((ParameterKey)pair.Key,
                Ice20Decoder.DecodeBuffer(pair.Value.ToArray(), decoder => decoder.DecodeVarULong())));

        internal static async ValueTask<(uint, InitializeBody?)> ReadInitializeAsync(
            this ISlicFrameReader reader,
            CancellationToken cancel)
        {
            (FrameType type, int frameSize, IMemoryOwner<byte> data) =
                await reader.ReadFrameAsync(cancel).ConfigureAwait(false);
            using IMemoryOwner<byte> _ = data;
            if (type != FrameType.Initialize)
            {
                throw new InvalidDataException($"unexpected Slic frame type {type}, expected {FrameType.Initialize}");
            }

            var decoder = new Ice20Decoder(data.Memory[0..frameSize]);
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
            CancellationToken cancel)
        {
            (FrameType type, int frameSize, IMemoryOwner<byte> data) =
                await reader.ReadFrameAsync(cancel).ConfigureAwait(false);
            using IMemoryOwner<byte> _ = data;
            if (type == FrameType.InitializeAck)
            {
                return (new InitializeAckBody(new Ice20Decoder(data.Memory[0..frameSize])), null);
            }
            else if (type == FrameType.Version)
            {
                return (null, new VersionBody(new Ice20Decoder(data.Memory[0..frameSize])));
            }
            else
            {
                throw new InvalidDataException($"unexpected Slic frame '{type}'");
            }
        }

        internal static ValueTask<StreamResetBody> ReadStreamResetAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadStreamDataFrameAsync(reader, dataSize, decoder => new StreamResetBody(decoder), cancel);

        internal static ValueTask<StreamConsumedBody> ReadStreamConsumedAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadStreamDataFrameAsync(reader, dataSize, decoder => new StreamConsumedBody(decoder), cancel);

        internal static ValueTask<StreamStopSendingBody> ReadStreamStopSendingAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            ReadStreamDataFrameAsync(reader, dataSize, decoder => new StreamStopSendingBody(decoder), cancel);

        internal static async ValueTask SkipStreamDataAsync(
            this ISlicFrameReader reader,
            int dataSize,
            CancellationToken cancel) =>
            (await reader.ReadStreamFrameDataAsync(dataSize, cancel).ConfigureAwait(false)).Dispose();

        internal static ValueTask WriteInitializeAsync(
            this ISlicFrameWriter writer,
            uint version,
            InitializeBody frame,
            CancellationToken cancel) =>
            writer.WriteFrameAsync(
                FrameType.Initialize,
                encoder =>
                {
                    encoder.EncodeVarUInt(version);
                    frame.Encode(encoder);
                },
                cancel);

        internal static ValueTask WriteInitializeAckAsync(
            this ISlicFrameWriter writer,
            InitializeAckBody frame,
            CancellationToken cancel) => writer.WriteFrameAsync(FrameType.InitializeAck, frame.Encode, cancel);

        internal static ValueTask WriteVersionAsync(
            this ISlicFrameWriter writer,
            VersionBody frame,
            CancellationToken cancel) => writer.WriteFrameAsync(FrameType.Version, frame.Encode, cancel);

        internal static ValueTask WriteStreamResetAsync(
            this ISlicFrameWriter writer,
            SlicStream stream,
            StreamResetBody frame,
            CancellationToken cancel) =>
            writer.WriteStreamFrameAsync(stream, FrameType.StreamReset, frame.Encode, cancel);

        internal static ValueTask WriteStreamConsumedAsync(
            this ISlicFrameWriter writer,
            SlicStream stream,
            StreamConsumedBody frame,
            CancellationToken cancel) =>
            writer.WriteStreamFrameAsync(stream, FrameType.StreamConsumed, frame.Encode, cancel);

        internal static ValueTask WriteStreamStopSendingAsync(
            this ISlicFrameWriter writer,
            SlicStream stream,
            StreamStopSendingBody frame,
            CancellationToken cancel) =>
            writer.WriteStreamFrameAsync(stream, FrameType.StreamStopSending, frame.Encode, cancel);

        private static async ValueTask<T> ReadStreamDataFrameAsync<T>(
            ISlicFrameReader reader,
            int size,
            Func<Ice20Decoder, T> decodeFunc,
            CancellationToken cancel)
        {
            using IMemoryOwner<byte> data = await reader.ReadStreamFrameDataAsync(size, cancel).ConfigureAwait(false);
            return decodeFunc(new Ice20Decoder(data.Memory));
        }
    }
}
