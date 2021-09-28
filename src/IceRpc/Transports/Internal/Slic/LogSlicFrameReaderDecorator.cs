// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Slic;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The LogSlicFrameReaderDecorator is a decorator to log read Slic frames.</summary>
    internal sealed class LogSlicFrameReaderDecorator : ISlicFrameReader
    {
        private ReadOnlyMemory<byte> _buffer;
        private readonly ISlicFrameReader _decoratee;
        private FrameType _frameType;
        private int _frameDataSize;
        private long _frameStreamId;
        private readonly ILogger _logger;
        private readonly ISlicFrameReader _reader;

        public void Dispose() => _decoratee.Dispose();

        public async ValueTask<(FrameType, int, IMemoryOwner<byte>)> ReadFrameAsync(CancellationToken cancel)
        {
            (FrameType type, int frameSize, IMemoryOwner<byte> owner) =
                await _decoratee.ReadFrameAsync(cancel).ConfigureAwait(false);
            LogReadFrame(type, frameSize, null, owner.Memory[0..frameSize]);
            return (type, frameSize, owner);
        }

        public ValueTask ReadStreamFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _decoratee.ReadStreamFrameDataAsync(buffer, cancel);

        public async ValueTask<IMemoryOwner<byte>> ReadStreamFrameDataAsync(int length, CancellationToken cancel)
        {
            IMemoryOwner<byte> owner = await _decoratee.ReadStreamFrameDataAsync(length, cancel).ConfigureAwait(false);
            LogReadFrame(_frameType, _frameDataSize, _frameStreamId, owner.Memory[0.._frameDataSize]);
            return owner;
        }

        public async ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel)
        {
            (_frameType, _frameDataSize, _frameStreamId) =
                await _decoratee.ReadStreamFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (_frameType == FrameType.Stream || _frameType == FrameType.StreamLast)
            {
                _logger.LogReceivingSlicDataFrame(_frameType, _frameDataSize);
            }
            return (_frameType, _frameDataSize, _frameStreamId);
        }

        internal LogSlicFrameReaderDecorator(ISlicFrameReader decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(ReceiveAsync, 256));
        }

        private void LogReadFrame(FrameType type, int dataSize, long? streamId, ReadOnlyMemory<byte> buffer)
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
            bufferWriter.WriteByteSpan(buffer.Span);
            int frameSize = encoder.EndFixedLengthSize(sizePos);

            // Assign the received frame data to the buffer. The reader will read the frame from this buffer.
            // The reading from the buffer will always complete synchronously so we don't need to await the
            // read async call in the switch bellow.  The Slic header is encoded in the first segment of the
            // buffer.
            _buffer = bufferWriter.Finish().Span[0];

            // Log the read frame
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => _reader.ReadInitializeAsync(default));
                    if (initializeBody == null)
                    {
                        _logger.LogReceivedSlicUnsupportedInitializeFrame(frameSize, version);
                    }
                    else
                    {
                        _logger.LogReceivedSlicInitializeFrame(frameSize, version, initializeBody.Value);
                    }
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => _reader.ReadInitializeAckOrVersionAsync(default));
                    if (initializeAckBody != null)
                    {
                        _logger.LogReceivedSlicInitializeAckFrame(frameSize, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogReceivedSlicVersionFrame(frameSize, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody body = ReadFrame(() => _reader.ReadStreamResetAsync(dataSize, default));
                    _logger.LogReceivedSlicResetFrame(frameSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody body = ReadFrame(() => _reader.ReadStreamConsumedAsync(dataSize, default));
                    _logger.LogReceivedSlicConsumedFrame(frameSize, (int)body.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody body = ReadFrame(() => _reader.ReadStreamStopSendingAsync(dataSize, default));
                    _logger.LogReceivedSlicStopSendingFrame(frameSize, (StreamError)body.ApplicationProtocolErrorCode);
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
