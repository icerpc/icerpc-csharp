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
    private protected bool _disposed;

    private readonly PipeWriter _writer;

    public abstract Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        if (_reader is null)
        {
            throw new InvalidOperationException("Reading is not allowed before connection is connected.");
        }

        ReadResult readResult = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        Debug.Assert(!readResult.IsCanceled);

        if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
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
        if (_disposed)
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        if (_reader is null)
        {
            throw new InvalidOperationException("Shutdown is not allowed before the connection is connected.");
        }

        // The FlushAsync is a no-op here since no data if buffered on the writer. It's only useful for ensuring that
        // ShutdownAsync is not called while WriteAsync is pending.
        _ = await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        _writer.Complete();
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        if (_reader is null)
        {
            throw new InvalidOperationException("Writing is not allowed before the connection is connected.");
        }

        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            FlushResult flushResult = await _writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            Debug.Assert(!flushResult.IsCanceled);
        }
    }

    public ColocConnection(ServerAddress serverAddress, PipeWriter writer)
    {
        ServerAddress = serverAddress;
        _writer = writer;
    }

    private protected virtual void Dispose(bool disposing)
    {
        _disposed = true;

        // _reader can be null if connection establishment failed or didn't run.
        _reader?.Complete(new IceRpcException(IceRpcError.ConnectionAborted));

        _writer.Complete(new IceRpcException(IceRpcError.ConnectionAborted));
    }

    private protected TransportConnectionInformation FinishConnect()
    {
        Debug.Assert(_reader is not null);

        if (_disposed)
        {
            _reader.Complete();
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }

        var colocEndPoint = new ColocEndPoint(ServerAddress);
        return new TransportConnectionInformation(colocEndPoint, colocEndPoint, null);
    }
}

/// <summary>The colocated client connection class.</summary>
internal class ClientColocConnection : ColocConnection
{
    private readonly Func<PipeReader, CancellationToken, Task<PipeReader>> _connectAsync;
    private PipeReader? _localPipeReader;

    public override async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException($"{typeof(ColocConnection)}");
        }
        if (_reader is not null)
        {
            throw new InvalidOperationException($"Connection establishment can't be called twice.");
        }

        if (_localPipeReader is not null)
        {
            _reader = await _connectAsync(_localPipeReader, cancellationToken).WaitAsync(cancellationToken)
                .ConfigureAwait(false);
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
