// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameWriterDecorator is a decorator to log Slic frames written to the decorated Slic frame
    /// writer.</summary>
    internal sealed class LogSlicFrameWriterDecorator : ISlicFrameWriter
    {
        private readonly ISlicFrameWriter _decoratee;
        private readonly ILogger _logger;
        private readonly Pipe _pipe;

        public async ValueTask WriteFrameAsync(
            FrameType frameType,
            long? streamId,
            EncodeAction? encode,
            CancellationToken cancel)
        {
            _pipe.Writer.EncodeFrame(frameType, streamId, encode);
            await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);
            _pipe.Reader.TryRead(out ReadResult readResult);

            try
            {
                await _decoratee.WriteFrameAsync(frameType, streamId, encode, cancel).ConfigureAwait(false);

                LogSentFrame(readResult.Buffer);
            }
            catch (Exception exception)
            {
                _logger.LogSendSlicFrameFailure(frameType, exception);
                throw;
            }
            finally
            {
                _pipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }

        public async ValueTask WriteStreamFrameAsync(
            long streamId,
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel)
        {
            _pipe.Writer.EncodeStreamFrameHeader(streamId, (int)(source1.Length + source2.Length), endStream);
            await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);
            _pipe.Reader.TryRead(out ReadResult readResult);

            try
            {
                await _decoratee.WriteStreamFrameAsync(
                    streamId,
                    source1,
                    source2,
                    endStream,
                    cancel).ConfigureAwait(false);

                LogSentFrame(readResult.Buffer);
            }
            catch (Exception exception)
            {
                _logger.LogSendSlicFrameFailure(endStream ? FrameType.StreamLast : FrameType.Stream, exception);
                throw;
            }
            finally
            {
                _pipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }

        internal LogSlicFrameWriterDecorator(ISlicFrameWriter decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _pipe = new Pipe();
        }

        private void LogSentFrame(ReadOnlySequence<byte> sequence)
        {
            (FrameType type, int dataSize, long? streamId, long consumed) = sequence.DecodeHeader();
            ReadOnlyMemory<byte> buffer = sequence.Slice((int)consumed).ToArray();
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
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody consumedBody = buffer.DecodeStreamConsumed();
                    _logger.LogSentSlicConsumedFrame(dataSize, (int)consumedBody.Size);
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
