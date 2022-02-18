// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameWriterDecorator is a decorator to log Slic frames written to the decorated
    /// Slic frame writer.</summary>
    internal sealed class LogSlicFrameWriterDecorator : ISlicFrameWriter
    {
        private readonly ISlicFrameWriter _decoratee;
        private readonly ILogger _logger;

        public async ValueTask WriteFrameAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            try
            {
                await _decoratee.WriteFrameAsync(buffers, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogSendSlicFrameFailure((FrameType)buffers[0].Span[0], exception);
                throw;
            }
            LogSentFrame(buffers[0]);
        }

        internal LogSlicFrameWriterDecorator(ISlicFrameWriter decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        private void LogSentFrame(ReadOnlyMemory<byte> buffer)
        {
            var sequence = new ReadOnlySequence<byte>(buffer);
            (FrameType type, int dataSize, long? streamId, long consumed) = sequence.DecodeHeader();
            buffer = buffer[(int)consumed..];
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) = buffer.DecodeInitialize(type);
                    _logger.LogSentSlicInitializeFrame(dataSize, version, initializeBody!.Value);
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        buffer.DecodeInitializeAckOrVersion(type);
                    if (initializeAckBody != null)
                    {
                        _logger.LogSentSlicInitializeAckFrame(dataSize, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogSentSlicVersionFrame(dataSize, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.Stream:
                case FrameType.StreamLast:
                {
                    _logger.LogSentSlicFrame(type, dataSize);
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody resetBody = buffer.DecodeStreamReset();
                    _logger.LogSentSlicResetFrame(dataSize, resetBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamResumeWrite:
                {
                    StreamResumeWriteBody consumedBody = buffer.DecodeStreamResumeWrite();
                    _logger.LogSentSlicResumeWriteFrame(dataSize, (int)consumedBody.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody stopSendingBody = buffer.DecodeStreamStopSending();
                    _logger.LogSentSlicStopSendingFrame(dataSize, stopSendingBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.UnidirectionalStreamReleased:
                {
                    _logger.LogSentSlicUnidirectionalStreamReleasedFrame();
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
