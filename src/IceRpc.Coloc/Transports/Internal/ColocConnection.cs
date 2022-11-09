// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The colocated connection class to exchange data within the same process. The implementation copies the send
/// buffer into the receive buffer.</summary>
internal abstract class ColocConnection : IDuplexConnection
{
    public ServerAddress ServerAddress { get; }

    private protected PipeReader? _reader;
    private protected int _state;

    private readonly PipeWriter _writer;

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public virtual ValueTask DisposeAsync()
    {
        if (_state.TrySetFlag(State.Disposed))
        {
            // _reader can be null if connection establishment failed or didn't run.
            if (_reader is not null)
            {
                if (_state.HasFlag(State.Reading))
                {
                    _reader.CancelPendingRead();
                }
                else
                {
                    _reader.Complete(new TransportException(TransportErrorCode.ConnectionReset));
                }
            }

            if (_state.HasFlag(State.Writing))
            {
                _writer.CancelPendingFlush();
            }
            else
            {
                _writer.Complete(new TransportException(TransportErrorCode.ConnectionReset));
            }
        }
        return new ValueTask();
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
                throw new TransportException(TransportErrorCode.ConnectionReset);
            }

            ReadResult readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                // Dispose canceled ReadAsync.
                throw new TransportException(TransportErrorCode.ConnectionReset);
            }
            else if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
            {
                return 0;
            }

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
                if (_state.HasFlag(State.ShuttingDown))
                {
                    throw new TransportException(TransportErrorCode.ConnectionShutdown);
                }

                FlushResult flushResult = await _writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (flushResult.IsCanceled)
                {
                    // Dispose canceled ReadAsync.
                    throw new TransportException(TransportErrorCode.ConnectionReset);
                }
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

    public ColocConnection(ServerAddress serverAddress, PipeWriter writer)
    {
        ServerAddress = serverAddress;
        _writer = writer;
    }

    private protected TransportConnectionInformation FinishConnect()
    {
        Debug.Assert(_reader is not null);

        if (_state.HasFlag(State.Disposed))
        {
            _reader.Complete();
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        else if (_state.HasFlag(State.ShuttingDown))
        {
            // Dispose will complete the reader.
            throw new TransportException(TransportErrorCode.ConnectionShutdown);
        }

        var colocEndPoint = new ColocEndPoint(ServerAddress);
        return new TransportConnectionInformation(colocEndPoint, colocEndPoint, null);
    }

    private protected enum State : int
    {
        Disposed = 1,
        Reading = 2,
        ShuttingDown = 4,
        Writing = 8,
    }
}

/// <summary>The colocated client connection class.</summary>
internal class ClientColocConnection : ColocConnection
{
    private readonly Func<PipeReader, CancellationToken, Task<PipeReader>> _connectAsync;
    private PipeReader? _localPipeReader;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_state.HasFlag(State.Disposed))
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        else if (_state.HasFlag(State.ShuttingDown))
        {
            throw new TransportException(TransportErrorCode.ConnectionShutdown);
        }

        if (_localPipeReader is not null)
        {
            _reader = await _connectAsync(_localPipeReader, cancellationToken).ConfigureAwait(false);
            _localPipeReader = null; // The server-side connection is now responsible for completing the pipe reader.
        }
        return FinishConnect();
    }

    public override ValueTask DisposeAsync()
    {
        _localPipeReader?.Complete();
        return base.DisposeAsync();
    }

    internal ClientColocConnection(
        ServerAddress serverAddress,
        Pipe localPipe,
        Func<PipeReader, CancellationToken, Task<PipeReader>> connectAsync)
        : base(serverAddress, localPipe.Writer)
    {
        _connectAsync = connectAsync;
        _localPipeReader = localPipe.Reader;
    }
}

/// <summary>The colocated server connection class.</summary>
internal class ServerColocConnection : ColocConnection
{
    public override Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FinishConnect());

    public ServerColocConnection(ServerAddress serverAddress, PipeWriter writer, PipeReader reader)
       : base(serverAddress, writer) => _reader = reader;
}
