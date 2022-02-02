// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
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
            FrameType type;
            int dataSize;
            long? streamId = null;

            using var reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffer));
            (type, dataSize, streamId) = ReadFrame(() => reader.ReadFrameHeaderAsync(default));

            int frameSize = dataSize;
            if (streamId != null)
            {
                frameSize += SliceEncoder.GetVarULongEncodedSize((ulong)streamId);
            }

            // Log the received frame.
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => reader.ReadInitializeAsync(type, dataSize, default));
                    _logger.LogSentSlicInitializeFrame(dataSize, version, initializeBody!.Value);
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => reader.ReadInitializeAckOrVersionAsync(type, dataSize, default));
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
                    StreamResetBody resetBody = ReadFrame(() => reader.ReadStreamResetAsync(dataSize, default));
                    _logger.LogSentSlicResetFrame(dataSize, resetBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamResumeWrite:
                {
                    StreamResumeWriteBody consumedBody = ReadFrame(() => reader.ReadStreamResumeWriteAsync(dataSize, default));
                    _logger.LogSentSlicResumeWriteFrame(dataSize, (int)consumedBody.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody stopSendingBody =
                        ReadFrame(() => reader.ReadStreamStopSendingAsync(dataSize, default));
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
                    Debug.Assert(false, $"failed to read Slic frame\n{ex}");
                    return default;
                }
            }
        }
    }
}
