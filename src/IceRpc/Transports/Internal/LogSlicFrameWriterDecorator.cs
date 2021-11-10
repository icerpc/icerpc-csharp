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

        public async ValueTask WriteFrameAsync(
            SlicMultiplexedStream? stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            using IDisposable? scope = stream == null ? null : _logger.StartStreamScope(stream.Id);
            try
            {
                await _decoratee.WriteFrameAsync(stream, buffers, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogSendSlicFrameFailure((FrameType)buffers.Span[0].Span[0], exception);
                throw;
            }
            LogSentFrame(buffers);
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicMultiplexedStream stream,
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            using IDisposable? scope = _logger.StartStreamScope(stream.Id);
            int frameSize = buffers.GetByteCount() - SlicDefinitions.FrameHeader.Length;
            _logger.LogSendingSlicFrame(endStream ? FrameType.StreamLast : FrameType.Stream, frameSize);
            try
            {
                await _decoratee.WriteStreamFrameAsync(stream, buffers, endStream, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogSendSlicFrameFailure(endStream ? FrameType.StreamLast : FrameType.Stream, exception);
                throw;
            }
        }

        internal LogSlicFrameWriterDecorator(ISlicFrameWriter decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        private void LogSentFrame(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            FrameType type;
            int dataSize;
            long? streamId = null;

            using var reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffers.Span[0]));
            (type, dataSize, streamId) = ReadFrame(() => reader.ReadFrameHeaderAsync(default));

            int frameSize = dataSize;
            if (streamId != null)
            {
                frameSize += IceEncoder.GetVarULongEncodedSize((ulong)streamId);
            }

            // Log the received frame.
            switch (type)
            {
                case FrameType.Close:
                {
                    _logger.LogSentSlicCloseFrame(dataSize);
                    break;
                }
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
                case FrameType.StreamReset:
                {
                    StreamResetBody resetBody = ReadFrame(() => reader.ReadStreamResetAsync(dataSize, default));
                    _logger.LogSentSlicResetFrame(dataSize, (StreamError)resetBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody consumedBody = ReadFrame(() => reader.ReadStreamConsumedAsync(dataSize, default));
                    _logger.LogSentSlicConsumedFrame(dataSize, (int)consumedBody.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody stopSendingBody =
                        ReadFrame(() => reader.ReadStreamStopSendingAsync(dataSize, default));
                    _logger.LogSentSlicStopSendingFrame(
                        dataSize,
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
                    Debug.Assert(false, $"failed to read Slic frame\n{ex}");
                    return default;
                }
            }
        }
    }
}
