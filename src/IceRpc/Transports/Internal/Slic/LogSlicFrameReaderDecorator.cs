// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
        private int _frameSize;
        private long _frameStreamId;
        private readonly ILogger _logger;
        private readonly ISlicFrameReader _reader;

        public void Dispose() => _decoratee.Dispose();

        public async ValueTask<(FrameType, int, IMemoryOwner<byte>)> ReadFrameAsync(CancellationToken cancel)
        {
            (FrameType type, int frameSize, IMemoryOwner<byte> owner) =
                await _decoratee.ReadFrameAsync(cancel).ConfigureAwait(false);
            LogReadFrame(owner.Memory, type, frameSize);
            return (type, frameSize, owner);
        }

        public ValueTask ReadStreamFrameDataAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _decoratee.ReadStreamFrameDataAsync(buffer, cancel);

        public async ValueTask<IMemoryOwner<byte>> ReadStreamFrameDataAsync(int length, CancellationToken cancel)
        {
            IMemoryOwner<byte> owner = await _decoratee.ReadStreamFrameDataAsync(length, cancel).ConfigureAwait(false);
            LogReadFrame(owner.Memory, _frameType, length);
            return owner;
        }

        public async ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel)
        {
            (_frameType, _frameSize, _frameStreamId) =
                await _decoratee.ReadStreamFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (_frameType == FrameType.Stream || _frameType == FrameType.StreamLast)
            {
                _logger.LogReceivingSlicDataFrame(_frameType, _frameSize);

            }
            return (_frameType, _frameSize, _frameStreamId);
        }

        internal LogSlicFrameReaderDecorator(ISlicFrameReader decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(ReceiveAsync, 256));
        }

        private void LogReadFrame(ReadOnlyMemory<byte> buffer, FrameType type, int size)
        {
            // Assign the received frame data to the buffer. The reader will read the frame from this buffer.
            // The reading from the buffer will always complete synchronously so we don't need to await the
            // read async call in the switch bellow.
            _buffer = buffer;

            // Log the read frame
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => _reader.ReadInitializeAsync(default));
                    if (initializeBody == null)
                    {
                        _logger.LogReceivedSlicUnsupportedInitializeFrame(size, version);
                    }
                    else
                    {
                        _logger.LogReceivingSlicInitializeFrame(size, version, initializeBody.Value);
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
                        _logger.LogReceivedSlicInitializeAckFrame(size, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogReceivedSlicVersionFrame(size, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody body = ReadFrame(() => _reader.ReadStreamResetAsync(_frameSize, default));
                    _logger.LogReceivedSlicResetFrame(_frameSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody body = ReadFrame(() => _reader.ReadStreamConsumedAsync(_frameSize, default));
                    _logger.LogReceivedSlicConsumedFrame(_frameSize, (int)body.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody body =
                        ReadFrame(() => _reader.ReadStreamStopSendingAsync(_frameSize, default));
                    _logger.LogReceivedSlicStopSendingFrame(_frameSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                default:
                    break;
            }

            static T ReadFrame<T>(Func<ValueTask<T>> readFunc)
            {
                ValueTask<T> task = readFunc();
                Debug.Assert(task.IsCompleted);
                return task.Result;
            }
        }

        private ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length > _buffer.Length)
            {
                throw new InvalidOperationException("not enough data to read the Slic frame");
            }
            _buffer.CopyTo(buffer);
            _buffer = _buffer[buffer.Length..];
            return new(buffer.Length);
        }
    }
}
