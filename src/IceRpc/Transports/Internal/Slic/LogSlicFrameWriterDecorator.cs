// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Slic;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The LogSlicFrameWriterDecorator is a Slic frame writer decorator to log written Slic
    /// frames.</summary>
    internal sealed class LogSlicFrameWriterDecorator : ISlicFrameWriter
    {
        private readonly ISlicFrameWriter _decoratee;
        private readonly ILogger _logger;

        public void Dispose() => _decoratee.Dispose();

        public async ValueTask WriteFrameAsync(
            SlicStream? stream,
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
            }
            LogSentFrame(buffers);
        }

        public async ValueTask WriteStreamFrameAsync(
            SlicStream stream,
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
            int size;
            long? streamId = null;

            var headerReader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffers.Span[0]));
            BufferedReceiverSlicFrameReader reader;
            int frameSize;
            if ((FrameType)buffers.Span[0].Span[0] < FrameType.Stream)
            {
                (type, size) = ReadFrame(() => headerReader.ReadFrameHeaderAsync(default));
                headerReader.Dispose();

                // The methods to read non-stream frames read the header, so we reset the buffer to allow
                // reading the header again.
                reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffers.Span[0]));
                frameSize = size;
            }
            else
            {
                (type, size, streamId) = ReadFrame(() => headerReader.ReadStreamFrameHeaderAsync(default));
                reader = headerReader;
                frameSize = size + IceEncoder.GetVarULongEncodedSize((ulong)streamId);
            }
            using BufferedReceiverSlicFrameReader _ = reader;

            // Log the received frame.
            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => reader.ReadInitializeAsync(default));
                    _logger.LogSentSlicInitializeFrame(size, version, initializeBody!.Value);
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => reader.ReadInitializeAckOrVersionAsync(default));
                    if (initializeAckBody != null)
                    {
                        _logger.LogSentSlicInitializeAckFrame(size, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogSentSlicVersionFrame(size, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody resetBody = ReadFrame(() => reader.ReadStreamResetAsync(size, default));
                    _logger.LogSentSlicResetFrame(size, (StreamError)resetBody.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody consumedBody = ReadFrame(() => reader.ReadStreamConsumedAsync(size, default));
                    _logger.LogSentSlicConsumedFrame(size, (int)consumedBody.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody stopSendingBody =
                        ReadFrame(() => reader.ReadStreamStopSendingAsync(size, default));
                    _logger.LogSentSlicStopSendingFrame(
                        size,
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
