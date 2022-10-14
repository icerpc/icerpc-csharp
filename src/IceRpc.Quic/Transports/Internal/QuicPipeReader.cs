// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicPipeReader : PipeReader
{
    private Exception? _abortException;
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private readonly PipeReader _pipeReader;
    private readonly QuicStream _stream;

    // StreamPipeReader.AdvanceTo does not call the underlying stream and as a result does not throw any QuicException.
    public override void AdvanceTo(SequencePosition consumed) => _pipeReader.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipeReader.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _pipeReader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        // This does not call _stream.Dispose since leaveOpen is set to true. The current implementation of
        // StreamPipeReader doesn't use the exception and it's unclear how it could use it.
        _pipeReader.Complete(exception);

        // We need to call Abort even when exception is null. Otherwise, the stream.DisposeAsync() sends the default
        // stream error code configured in Quic(Client,Server)ConnectionOptions.
        Abort(exception);

        // Notify the stream of the reader completion, which can trigger the stream disposal.
        _completedCallback();
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        Task<ReadResult>? task = null;
        try
        {
            task = _pipeReader.ReadAsync(CancellationToken.None).AsTask();
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // We can't let task run in the background - it's not ok to call Complete or any other method on PipeReader
            // (except CancelPendingRead) while a ReadAsync is running in a separate thread. So we need to abort the
            // stream and wait for task to complete.
            Debug.Assert(task is not null);
            Abort(exception);
            try
            {
                _ = await task.ConfigureAwait(false);
            }
            catch
            {
            }
            throw;
        }
        catch when (Volatile.Read(ref _abortException) is Exception abortException)
        {
            throw abortException;
        }
        catch (QuicException exception) when (
            exception.QuicError == QuicError.StreamAborted &&
            exception.ApplicationErrorCode is not null)
        {
            throw _errorCodeConverter.FromErrorCode((ulong)exception.ApplicationErrorCode)!;
        }
        catch (QuicException exception) when (exception.QuicError == QuicError.ConnectionAborted)
        {
            // If the connection is closed before the stream. This indicates that the peer forcefully closed the
            // connection (it called DisposeAsync before completing the streams).
            throw new TransportException(TransportErrorCode.ConnectionReset, exception);
        }
        catch (QuicException exception)
        {
            throw exception.ToTransportException();
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
        }
    }

    // StreamPipeReader.TryRead does not call the underlying stream and as a result does not throw any QuicException.
    // TODO: should be throw _abortException if not null?
    public override bool TryRead(out ReadResult result) => _pipeReader.TryRead(out result);

    internal QuicPipeReader(
        QuicStream stream,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        Action completedCallback)
    {
        _stream = stream;
        _errorCodeConverter = errorCodeConverter;
        _completedCallback = completedCallback;

        _pipeReader = Create(
            _stream,
            new StreamPipeReaderOptions(pool, minimumSegmentSize, minimumReadSize: -1, leaveOpen: true));
    }

    // Note that the exception has 2 separate purposes: transmit an error code to the peer and throw this exception
    // from the current or next ReadAsync.
    internal void Abort(Exception? exception)
    {
        if (Interlocked.CompareExchange(ref _abortException, exception, null) is null)
        {
            // _abortException was null before this call, which means we did not abort it yet.
            _stream.Abort(QuicAbortDirection.Read, (long)_errorCodeConverter.ToErrorCode(exception));
        }
    }
}
