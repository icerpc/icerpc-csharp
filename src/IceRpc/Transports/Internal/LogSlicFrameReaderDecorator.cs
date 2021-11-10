// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameReaderDecorator is a decorator to log Slic frames read from the
    /// decorated Slic frame reader.</summary>
    internal sealed class LogSlicFrameReaderDecorator : ISlicFrameReader
    {
        private readonly ISlicFrameReader _decoratee;
        private FrameType _frameType;
        private int _frameDataSize;
        private long? _frameStreamId;
        private readonly ILogger _logger;

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            await _decoratee.ReadFrameDataAsync(buffer, cancel).ConfigureAwait(false);
            if (_frameType != FrameType.Stream && _frameType != FrameType.StreamLast)
            {
                if (_frameStreamId == null)
                {
                    LogReadFrame(_frameType, _frameDataSize, null, buffer);
                }
                else
                {
                    using IDisposable? scope = _logger.StartStreamScope(_frameStreamId.Value);
                    LogReadFrame(_frameType, _frameDataSize, _frameStreamId, buffer);
                }
            }
        }

        public async ValueTask<(FrameType, int, long?)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            (_frameType, _frameDataSize, _frameStreamId) =
                 await _decoratee.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);

            if (_frameType == FrameType.Stream || _frameType == FrameType.StreamLast)
            {
                using IDisposable? scope = _logger.StartStreamScope(_frameStreamId!.Value);
                _logger.LogReceivingSlicDataFrame(_frameType, _frameDataSize);
            }
            else if (_frameType == FrameType.Close)
            {
                // The Close frame doesn't have any data, log it now because ReadFrameDataAsync won't be called.
                _logger.LogReceivedSlicCloseFrame(_frameDataSize);
            }

            return (_frameType, _frameDataSize, _frameStreamId);
        }

        internal LogSlicFrameReaderDecorator(ISlicFrameReader decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        private void LogReadFrame(FrameType type, int dataSize, long? streamId, ReadOnlyMemory<byte> buffer)
        {
            // Create a reader to read again the frame from the memory buffer.
            using var reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffer));

            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => reader.ReadInitializeAsync(type, dataSize, default));
                    if (initializeBody == null)
                    {
                        _logger.LogReceivedSlicUnsupportedInitializeFrame(dataSize, version);
                    }
                    else
                    {
                        _logger.LogReceivedSlicInitializeFrame(dataSize, version, initializeBody.Value);
                    }
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => reader.ReadInitializeAckOrVersionAsync(type, dataSize, default));
                    if (initializeAckBody != null)
                    {
                        _logger.LogReceivedSlicInitializeAckFrame(dataSize, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogReceivedSlicVersionFrame(dataSize, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody body = ReadFrame(() => reader.ReadStreamResetAsync(dataSize, default));
                    _logger.LogReceivedSlicResetFrame(dataSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody body = ReadFrame(() => reader.ReadStreamConsumedAsync(dataSize, default));
                    _logger.LogReceivedSlicConsumedFrame(dataSize, (int)body.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody body = ReadFrame(() => reader.ReadStreamStopSendingAsync(dataSize, default));
                    _logger.LogReceivedSlicStopSendingFrame(dataSize, (StreamError)body.ApplicationProtocolErrorCode);
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
                    // The reading of the frame should always complete synchronously since we're reading the
                    // frame from a memory buffer.
                    ValueTask<T> task = readFunc();
                    Debug.Assert(task.IsCompleted);
                    return task.Result;
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"failed to read Slic frame\n{ex}");
                    return default;
                }
            }
        }
    }
}
