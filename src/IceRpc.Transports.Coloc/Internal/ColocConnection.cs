// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Coloc.Internal;

/// <summary>The colocated connection class to exchange data within the same process. The implementation copies the send
/// buffer into the receive buffer.</summary>
internal abstract class ColocConnection : IDuplexConnection
{
    private protected PipeReader? _reader;
    // FlagEnumExtensions operations are used to update the state. These operations are atomic and don't require mutex
    // locking.
    private protected int _state;

    private readonly ServerAddress _serverAddress;
    private readonly PipeWriter _writer;

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_state.HasFlag(State.Disposed), this);

        if (_reader is null)
        {
            throw new InvalidOperationException("Reading is not allowed before connection is connected.");
        }
        if (!_state.TrySetFlag(State.Reading))
        {
            throw new InvalidOperationException("Reading is already in progress.");
        }

        try
        {
            ReadResult readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                // Dispose canceled ReadAsync.
                throw new IceRpcException(IceRpcError.OperationAborted);
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
                _reader.Complete(new IceRpcException(IceRpcError.ConnectionAborted));
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

    public Task ShutdownWriteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_state.HasFlag(State.Disposed), this);

        if (_reader is null)
        {
            throw new InvalidOperationException("Shutdown is not allowed before the connection is connected.");
        }
        if (_state.HasFlag(State.Writing))
        {
            throw new InvalidOperationException("Shutdown or writing is in progress.");
        }
        if (!_state.TrySetFlag(State.ShuttingDown))
        {
            throw new InvalidOperationException("Shutdown has already been called.");
        }

        _writer.Complete();
        return Task.CompletedTask;
    }

    public async ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_state.HasFlag(State.Disposed), this);

        if (buffer.IsEmpty)
        {
            throw new ArgumentException($"The {nameof(buffer)} cannot be empty.", nameof(buffer));
        }

        if (_reader is null)
        {
            throw new InvalidOperationException("Writing is not allowed before the connection is connected.");
        }
        if (_state.HasFlag(State.ShuttingDown))
        {
            throw new InvalidOperationException("Writing is not allowed after the connection is shutdown.");
        }
        if (!_state.TrySetFlag(State.Writing))
        {
            throw new InvalidOperationException("Writing is already in progress.");
        }

        try
        {
            _writer.Write(buffer);
            FlushResult flushResult = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (flushResult.IsCanceled)
            {
                // Dispose canceled ReadAsync.
                throw new IceRpcException(IceRpcError.OperationAborted);
            }
        }
        finally
        {
            Debug.Assert(!_state.HasFlag(State.ShuttingDown));
            if (_state.HasFlag(State.Disposed))
            {
                _writer.Complete(new IceRpcException(IceRpcError.ConnectionAborted));
            }
            _state.ClearFlag(State.Writing);
        }
    }

    public ColocConnection(ServerAddress serverAddress, PipeWriter writer)
    {
        _serverAddress = serverAddress;
        _writer = writer;
    }

    private protected virtual void Dispose(bool disposing)
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
                    _reader.Complete(new IceRpcException(IceRpcError.ConnectionAborted));
                }
            }

            if (_state.HasFlag(State.Writing))
            {
                _writer.CancelPendingFlush();
            }
            else
            {
                _writer.Complete(new IceRpcException(IceRpcError.ConnectionAborted));
            }
        }
    }

    private protected TransportConnectionInformation FinishConnect()
    {
        Debug.Assert(_reader is not null);

        if (_state.HasFlag(State.Disposed))
        {
            _reader.Complete();
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }

        var colocEndPoint = new ColocEndPoint(_serverAddress);
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
        ObjectDisposedException.ThrowIf(_state.HasFlag(State.Disposed), this);

        if (_reader is not null)
        {
            throw new InvalidOperationException("Connection establishment cannot be called twice.");
        }

        Debug.Assert(!_state.HasFlag(State.ShuttingDown));

        if (_localPipeReader is not null)
        {
            _reader = await _connectAsync(_localPipeReader, cancellationToken).ConfigureAwait(false);
            _localPipeReader = null; // The server-side connection is now responsible for completing the pipe reader.
        }
        return FinishConnect();
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

    private protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _localPipeReader?.Complete();
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
