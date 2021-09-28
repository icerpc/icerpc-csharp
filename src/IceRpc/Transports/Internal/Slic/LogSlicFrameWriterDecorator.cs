// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Slic;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The LogSlicFrameWriterDecorator is a decorator to log written Slic frames.</summary>
    internal sealed class LogSlicFrameWriterDecorator : ISlicFrameWriter
    {
        private ReadOnlyMemory<byte> _buffer;
        private readonly ISlicFrameWriter _decoratee;
        private readonly ILogger _logger;
        private readonly ISlicFrameReader _reader;

        public void Dispose() => _decoratee.Dispose();

        public async ValueTask WriteFrameAsync(FrameType type, Action<IceEncoder> encode, CancellationToken cancel)
        {
            await _decoratee.WriteFrameAsync(type, encode, cancel).ConfigureAwait(false);
            LogSentFrame(type, null, encode);
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            FrameType type,
            Action<IceEncoder> encode,
            CancellationToken cancel)
        {
            await _decoratee.WriteStreamFrameAsync(stream, type, encode, cancel).ConfigureAwait(false);
            LogSentFrame(type, stream.Id, encode);
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            int frameSize =
                buffers.GetByteCount() -
                SlicDefinitions.FrameHeader.Length +
                Ice20Encoder.GetSizeLength(stream.Id);
            _logger.LogSendingSlicFrame(endStream ? FrameType.StreamLast : FrameType.Stream, frameSize);
            await _decoratee.WriteStreamFrameAsync(stream, buffers, endStream, cancel).ConfigureAwait(false);
        }

        internal LogSlicFrameWriterDecorator(ISlicFrameWriter decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(ReceiveAsync, 256));
        }

        void LogSentFrame(FrameType type, long? streamId, Action<IceEncoder> encode)
        {
            // Encode the frame
            var bufferWriter = new BufferWriter();
            var encoder = new Ice20Encoder(bufferWriter);
            encoder.EncodeByte((byte)type);
            BufferWriter.Position sizePos = encoder.StartFixedLengthSize();
            if (streamId != null)
            {
                encoder.EncodeVarULong((ulong)streamId.Value);
            }
            encode(encoder);
            int frameSize = encoder.EndFixedLengthSize(sizePos);
            int dataSize = frameSize;
            if (streamId != null)
            {
                 dataSize -= Ice20Encoder.GetSizeLength(streamId.Value);
            }

            // Assign the encoded frame data to the buffer. The reader will read the frame from this buffer.
            // The reading from the buffer will always complete synchronously so we don't need to await the
            // read async call in the switch bellow. The Slic header is encoded in the first segment of the
            // buffer.
            _buffer = bufferWriter.Finish().Span[0];

            // Log the received frame.
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => _reader.ReadInitializeAsync(default));
                    _logger.LogSentSlicInitializeFrame(frameSize, version, initializeBody!.Value);
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => _reader.ReadInitializeAckOrVersionAsync(default));
                    if (initializeAckBody != null)
                    {
                        _logger.LogSentSlicInitializeAckFrame(frameSize, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogSentSlicVersionFrame(frameSize, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody resetBody = ReadFrame(() => _reader.ReadStreamResetAsync(frameSize, default));
                    _logger.LogSentSlicResetFrame(frameSize, (StreamError)resetBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody consumedBody =
                        ReadFrame(() => _reader.ReadStreamConsumedAsync(frameSize, default));
                    _logger.LogSentSlicConsumedFrame(frameSize, (int)consumedBody.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody stopSendingBody =
                        ReadFrame(() => _reader.ReadStreamStopSendingAsync(frameSize, default));
                    _logger.LogSentSlicStopSendingFrame(
                        frameSize,
                        (StreamError)stopSendingBody.ApplicationProtocolErrorCode);
                    break;
                }
                default:
                {
                    Debug.Assert(false, $"unexpected Slic frame {type}");
                    break;
                }
            }

            static T ReadFrame<T>(Func<ValueTask<T>> readFunc)
            {
                try
                {
                    ValueTask<T> task = readFunc();
                    Debug.Assert(task.IsCompleted);
                    return task.Result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Debug.Assert(false, $"failed to read Slic frame\n{ex}");
                    return default;
                }
            }
        }

        private ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_buffer.Length <= buffer.Length)
            {
                _buffer.CopyTo(buffer);
                int size = _buffer.Length;
                _buffer = ReadOnlyMemory<byte>.Empty;
                return new(size);
            }
            else
            {
                _buffer[0..buffer.Length].CopyTo(buffer);
                _buffer = _buffer[buffer.Length..];
                return new(buffer.Length);
            }
        }
    }
}
