// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameReaderDecorator is a decorator to log Slic frames read from the decorated Slic frame
    /// reader.</summary>
    internal sealed class LogSlicFrameReaderDecorator : ISlicFrameReader
    {
        public SimpleNetworkConnectionReader NetworkConnectionReader => _decoratee.NetworkConnectionReader;

        private readonly ISlicFrameReader _decoratee;
        private FrameType _frameType;
        private int _frameDataSize;
        private long? _frameStreamId;
        private readonly ILogger _logger;

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

        // TODO: this method is not used anywhere
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
                case FrameType.Close:
                {
                    var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
                    var closeBody = new CloseBody(ref decoder);
                    _logger.LogReceivedSlicCloseFrame(dataSize, closeBody.ApplicationProtocolErrorCode);
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
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody body = buffer.DecodeStreamConsumed();
                    _logger.LogReceivedSlicConsumedFrame(dataSize, (int)body.Size);
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
