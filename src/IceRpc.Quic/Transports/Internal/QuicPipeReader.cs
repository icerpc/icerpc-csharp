// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>Implements a PipeReader over a QuicStream.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicPipeReader : PipeReader
{
    internal Task ReadsClosed { get; }

    private Exception? _abortException;
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private bool _isCompleted;
    private readonly PipeReader _pipeReader;
    private readonly TaskCompletionSource _readsCompleteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ReadResult _readResult; // most recent readResult
    private readonly QuicStream _stream;

    // StreamPipeReader.AdvanceTo does not call the underlying stream and as a result does not throw any QuicException.
    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (_readResult.IsCompleted &&
            _readResult.Buffer.GetOffset(consumed) == _readResult.Buffer.GetOffset(_readResult.Buffer.End))
        {
            _ = _readsCompleteTcs.TrySetResult();
        }

        _pipeReader.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead() => _pipeReader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            // This does not call _stream.Dispose since leaveOpen is set to true. The current implementation of
            // StreamPipeReader doesn't use the exception and it's unclear how it could use it.
            _pipeReader.Complete(exception);
            _ = _readsCompleteTcs.TrySetResult();

            if (exception is null)
            {
                if (!_stream.ReadsClosed.IsCompleted)
                {
                    // Tell the remote writer we're done reading, with the error code of a null exception. This also
                    // completes _stream.ReadsClosed.
                    _stream.Abort(QuicAbortDirection.Read, (long)_errorCodeConverter.ToErrorCode(null));
                }
            }
            else
            {
                Abort(exception);
            }

            // Notify the stream of the reader completion, which can trigger the stream disposal.
            _completedCallback();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _readResult = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return _readResult;
        }
        catch (QuicException) when (Volatile.Read(ref _abortException) is Exception abortException)
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

            // TODO: this is ultra confusing when you see a stack trace with ConnectionReset and the inner
            // QuicException is "Connection aborted".
            throw new TransportException(TransportErrorCode.ConnectionReset, exception);
        }
        catch (QuicException exception)
        {
            throw exception.ToTransportException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    // StreamPipeReader.TryRead does not call the underlying stream and as a result does not throw any QuicException.
    public override bool TryRead(out ReadResult result)
    {
        if (_pipeReader.TryRead(out result))
        {
            _readResult = result;
            return true;
        }
        else
        {
            return false;
        }
    }

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

        ReadsClosed = CreateReadsClosedTask();

        async Task CreateReadsClosedTask()
        {
            try
            {
                await _stream.ReadsClosed.ConfigureAwait(false);
            }
            catch (QuicException exception)
            {
                throw exception.ToTransportException();
            }
            // we don't wrap other exceptions

            await _readsCompleteTcs.Task.ConfigureAwait(false);
        }
    }

    // The exception has 2 separate purposes: transmit an error code to the remote reader and throw this exception from
    // the current or next ReadAsync.
    internal void Abort(Exception exception)
    {
        // If ReadsClosed is already completed or this is not the first call to Abort, there is nothing to abort.
        if (!_stream.ReadsClosed.IsCompleted &&
            Interlocked.CompareExchange(ref _abortException, exception, null) is null)
        {
            _stream.Abort(QuicAbortDirection.Read, (long)_errorCodeConverter.ToErrorCode(exception));
        }

        // We also complete _readsCompleteTcs no matter what. This is useful in the situation where Abort is called
        // after_stream.ReadsClosed completed or while it's completing.
        _readsCompleteTcs.TrySetException(exception);
    }
}
