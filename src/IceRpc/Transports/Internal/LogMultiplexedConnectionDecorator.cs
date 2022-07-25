// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

internal sealed class LogMultiplexedConnectionDecorator : IMultiplexedConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private readonly IMultiplexedConnection _decoratee;
    private readonly ILogger _logger;

    // We don't log anything as it would be redundant with the ProtocolConnection logging.
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel) =>
        _decoratee.ConnectAsync(cancel);

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
        new LogMultiplexedStreamDecorator(
            await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false),
            _logger);

    public IMultiplexedStream CreateStream(bool bidirectional) =>
        new LogMultiplexedStreamDecorator(_decoratee.CreateStream(bidirectional), _logger);

    // We don't log anything as it would be redundant with the ProtocolConnection logging.
    public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

    public async Task ShutdownAsync(Exception exception, CancellationToken cancel)
    {
        // TODO: do we always get the scope from ProtocolConnection?

        await _decoratee.ShutdownAsync(exception, cancel).ConfigureAwait(false);
        _logger.LogMultiplexedConnectionShutdown(exception);
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogMultiplexedConnectionDecorator(IMultiplexedConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}

internal sealed class LogMultiplexedStreamDecorator : IMultiplexedStream
{
    public long Id => _decoratee.Id;

    public PipeReader Input => _input ??= new LogMultiplexedStreamPipeReader(_decoratee.Input, this, _logger);

    public bool IsBidirectional => _decoratee.IsBidirectional;

    public bool IsRemote => _decoratee.IsRemote;

    public bool IsStarted => _decoratee.IsStarted;

    public PipeWriter Output => _output ??= new LogMultiplexedStreamPipeWriter(_decoratee.Output, this, _logger);

    private readonly IMultiplexedStream _decoratee;
    private PipeReader? _input;
    private readonly ILogger _logger;
    private PipeWriter? _output;

    public void OnShutdown(Action callback) => _decoratee.OnShutdown(callback);

    public void OnPeerInputCompleted(Action callback) => _decoratee.OnPeerInputCompleted(callback);

    public void Abort(Exception exception) => _decoratee.Abort(exception);

    public override string? ToString() => _decoratee.ToString();

    internal LogMultiplexedStreamDecorator(IMultiplexedStream decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}

internal sealed class LogMultiplexedStreamPipeWriter : ReadOnlySequencePipeWriter
{
    private readonly PipeWriter _decoratee;
    private readonly ILogger _logger;
    private readonly IMultiplexedStream _stream;

    public override void Advance(int bytes)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.Advance(bytes);
    }

    public override void CancelPendingFlush()
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.CancelPendingFlush();
    }

    public override void Complete(Exception? exception)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.Complete(exception);
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        return await _decoratee.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancel)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        return await _decoratee.WriteAsync(source, cancel).ConfigureAwait(false);
    }

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancel)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        return await _decoratee.WriteAsync(source, endStream, cancel).ConfigureAwait(false);
    }

    internal LogMultiplexedStreamPipeWriter(PipeWriter decoratee, IMultiplexedStream stream, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
        _stream = stream;
    }
}

internal sealed class LogMultiplexedStreamPipeReader : PipeReader
{
    private readonly PipeReader _decoratee;
    private readonly IMultiplexedStream _stream;
    private readonly ILogger _logger;

    public override void AdvanceTo(SequencePosition consumed)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.AdvanceTo(consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead()
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.CancelPendingRead();
    }

    public override void Complete(Exception? exception = null)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        _decoratee.Complete(exception);
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        return await _decoratee.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public override bool TryRead(out ReadResult result)
    {
        using IDisposable _ = _logger.StartMultiplexedStreamScope(_stream);
        return _decoratee.TryRead(out result);
    }

    internal LogMultiplexedStreamPipeReader(PipeReader decoratee, IMultiplexedStream stream, ILogger logger)
    {
        _decoratee = decoratee;
        _stream = stream;
        _logger = logger;
    }
}
