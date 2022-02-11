// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameReaderDecorator is a decorator to log Slic frames read from the decorated Slic frame
    /// reader.</summary>
    internal sealed class LogSlicFrameReaderDecorator : ISlicFrameReader
    {
        private readonly ISlicFrameReader _decoratee;
        private FrameType _frameType;
        private int _frameDataSize;
        private long? _frameStreamId;
        private readonly ILogger _logger;

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            using IDisposable? _ = _frameStreamId == null ?
                null :
                _logger.StartMultiplexedStreamScope(_frameStreamId.Value);
            await _decoratee.ReadFrameDataAsync(buffer, cancel).ConfigureAwait(false);
            if (_frameType != FrameType.Stream && _frameType != FrameType.StreamLast)
            {
                LogReadFrame(_frameType, _frameDataSize, buffer);
            }
        }

        public async ValueTask<(FrameType FrameType, int FrameSize, long? StreamId)> ReadFrameHeaderAsync(
            CancellationToken cancel)
        {
            (_frameType, _frameDataSize, _frameStreamId) =
                await _decoratee.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            if (_frameType == FrameType.Stream || _frameType == FrameType.StreamLast)
            {
                using IDisposable? _ = _logger.StartMultiplexedStreamScope(_frameStreamId!.Value);
                _logger.LogReceivingSlicDataFrame(_frameType, _frameDataSize);
            }
            return (_frameType, _frameDataSize, _frameStreamId);
        }

        internal LogSlicFrameReaderDecorator(ISlicFrameReader decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        private void LogReadFrame(FrameType type, int dataSize, ReadOnlyMemory<byte> buffer)
        {
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) = buffer.DecodeInitialize(FrameType.Initialize);
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
                        buffer.DecodeInitializeAckOrVersion(type);
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
                case FrameType.Stream:
                case FrameType.StreamLast:
                {
                    _logger.LogReceivingSlicDataFrame(type, dataSize);
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody body = buffer.DecodeStreamReset();
                    _logger.LogReceivedSlicResetFrame(dataSize, body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamResumeWrite:
                {
                    StreamResumeWriteBody body = buffer.DecodeStreamResumeWrite();
                    _logger.LogReceivedSlicResumeWriteFrame(dataSize, (int)body.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody body = buffer.DecodeStreamStopSending();
                    _logger.LogReceivedSlicStopSendingFrame(dataSize, body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.UnidirectionalStreamReleased:
                {
                    _logger.LogReceivedSlicUnidirectionalStreamReleased();
                    break;
                }
                default:
                {
                    Debug.Assert(false, $"unexpected Slic frame {type}");
                    break;
                }
            }

        }
    }
}
