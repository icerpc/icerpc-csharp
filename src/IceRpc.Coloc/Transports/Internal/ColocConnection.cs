// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The colocated connection class to exchange data within the same process. The implementation copies the send
/// buffer into the receive buffer.</summary>
internal class ColocConnection : IDuplexConnection
{
    public ServerAddress ServerAddress { get; }

    private readonly Func<Task<PipeReader>> _connect;

    // Remember the failure that caused the connection failure to raise the same exception from WriteAsync or ReadAsync
    private Exception? _exception;
    private PipeReader? _reader;
    private int _state;
    private readonly PipeWriter _writer;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_state.HasFlag(State.Disposed))
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        else if (_state.HasFlag(State.ShuttingDown))
        {
            throw new TransportException(TransportErrorCode.ConnectionShutdown);
        }

        // Connect the collocated connection. This waits for the server-side to accept the connection.
        _reader = await _connect().WaitAsync(cancellationToken).ConfigureAwait(false);

        var colocEndPoint = new ColocEndPoint(ServerAddress);
        return new TransportConnectionInformation(colocEndPoint, colocEndPoint, null);
    }

    public void Dispose()
    {
        _exception ??= new TransportException(TransportErrorCode.ConnectionReset);

        if (_state.TrySetFlag(State.Disposed))
        {
            // _reader can be null if connection establishment failed.
            if (_reader is not null)
            {
                if (_state.HasFlag(State.Reading))
                {
                    _reader.CancelPendingRead();
                }
                else
                {
                    _reader.Complete(_exception);
                }
            }

            if (_state.HasFlag(State.Writing))
            {
                _writer.CancelPendingFlush();
            }
            else
            {
                _writer.Complete(_exception);
            }
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Debug.Assert(_reader is not null);

        if (_state.HasFlag(State.Disposed))
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }

        if (!_state.TrySetFlag(State.Reading))
        {
            throw new InvalidOperationException($"{nameof(ReadAsync)} is not thread safe");
        }

        try
        {
            if (_state.HasFlag(State.Disposed))
            {
                throw _exception!;
            }

            ReadResult readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
            {
                return 0;
            }

            if (_state.HasFlag(State.Disposed))
            {
                throw _exception!;
            }

            Debug.Assert(!readResult.IsCanceled);

            // We could eventually add a CopyTo(this ReadOnlySequence<byte> src, Memory<byte> dest) extension method
            // if we need this in other places.
            int read;
            if (readResult.Buffer.IsSingleSegment)
            {
                read = CopySegmentToMemory(readResult.Buffer.First, buffer);
            }
            else
            {
                read = 0;
                foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                {
                    read += CopySegmentToMemory(segment, buffer[read..]);
                    if (read == buffer.Length)
                    {
                        break;
                    }
                }
            }
            _reader.AdvanceTo(readResult.Buffer.GetPosition(read));
            return read;
        }
        finally
        {
            if (_state.HasFlag(State.Disposed))
            {
                await _reader.CompleteAsync(new TransportException(
                    TransportErrorCode.ConnectionDisposed)).ConfigureAwait(false);
            }
            _state.ClearFlag(State.Reading);
        }

        static int CopySegmentToMemory(ReadOnlyMemory<byte> source, Memory<byte> destination)
        {
            if (source.Length > destination.Length)
            {
                source[0..destination.Length].CopyTo(destination);
                return destination.Length;
            }
            else
            {
                source.CopyTo(destination);
                return source.Length;
            }
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (_state.HasFlag(State.Disposed))
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }

        if (_state.TrySetFlag(State.ShuttingDown))
        {
            if (_state.TrySetFlag(State.Writing))
            {
                await _writer.CompleteAsync().ConfigureAwait(false);
                _state.ClearFlag(State.Writing);
            }
            else
            {
                // WriteAsync will take care of completing the writer once it's done writing.
            }
        }
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        Debug.Assert(_reader is not null);

        if (_state.HasFlag(State.Disposed))
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }

        if (!_state.TrySetFlag(State.Writing))
        {
            if (_state.HasFlag(State.ShuttingDown))
            {
                throw new TransportException(TransportErrorCode.ConnectionShutdown);
            }
            else
            {
                throw new InvalidOperationException($"{nameof(WriteAsync)} is not thread safe");
            }
        }

        try
        {
            foreach (ReadOnlyMemory<byte> buffer in buffers)
            {
                if (_state.HasFlag(State.Disposed))
                {
                    throw _exception!;
                }
                else if (_state.HasFlag(State.ShuttingDown))
                {
                    throw new TransportException(TransportErrorCode.ConnectionShutdown);
                }

                _ = await _writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_state.HasFlag(State.Disposed))
            {
                await _writer.CompleteAsync(new TransportException(
                    TransportErrorCode.ConnectionDisposed)).ConfigureAwait(false);
            }
            else if (_state.HasFlag(State.ShuttingDown))
            {
                await _writer.CompleteAsync().ConfigureAwait(false);
            }
            _state.ClearFlag(State.Writing);
        }
    }

    public ColocConnection(ServerAddress serverAddress, PipeWriter writer, Func<Task<PipeReader>> connect)
    {
        ServerAddress = serverAddress;
        _writer = writer;
        _connect = connect;
    }

    private enum State : int
    {
        Disposed = 1,
        Reading = 2,
        ShuttingDown = 4,
        Writing = 8,
    }
}
