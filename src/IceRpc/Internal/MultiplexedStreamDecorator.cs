// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>Provides a multiplexed stream decorator that allows <see cref="IceRpcProtocolConnection" /> to intercept
/// the completion of the stream input and output.</summary>
internal sealed class MultiplexedStreamDecorator : IMultiplexedStream
{
    public ulong Id => _decoratee.Id;

    public bool IsBidirectional => _decoratee.IsBidirectional;

    public bool IsRemote => _decoratee.IsRemote;

    public bool IsStarted => _decoratee.IsStarted;

    public Task ReadsClosed => _decoratee.ReadsClosed;

    public Task WritesClosed => _decoratee.WritesClosed;

    public PipeReader Input => _input ?? _decoratee.Input;

    public PipeWriter Output => _output ?? _decoratee.Output;

    private readonly IMultiplexedStream _decoratee;
    private readonly PipeReader? _input;
    private readonly PipeWriter? _output;

    /// <summary>Constructs a multiplexed stream decorator.</summary>
    /// <param name="decoratee">The decoratee.</param>
    /// <param name="onCompleted">An action that is executed when stream input or output is completed; it's executed
    /// up to 2 times for a bidirectional stream, and up to 1 time for a unidirectional stream.</param>
    internal MultiplexedStreamDecorator(IMultiplexedStream decoratee, Action onCompleted)
    {
        _decoratee = decoratee;

        if (decoratee.IsBidirectional || decoratee.IsRemote)
        {
            _input = new InputDecorator(decoratee.Input, onCompleted);
        }
        if (decoratee.IsBidirectional || !decoratee.IsRemote)
        {
            _output = new OutputDecorator((ReadOnlySequencePipeWriter)decoratee.Output, onCompleted);
        }
    }

    private class InputDecorator : PipeReader
    {
        private readonly PipeReader _decoratee;
        private bool _isCompleted;
        private readonly Action _onCompleted;

        public override void AdvanceTo(SequencePosition consumed) =>
            _decoratee.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);

        // When leaveOpen is false, we use the default implementation of AsStream: we want the disposal of the stream
        // to call Complete on this decorator.
        public override Stream AsStream(bool leaveOpen = false) =>
            leaveOpen ? _decoratee.AsStream(leaveOpen) : base.AsStream(leaveOpen);

        public override void CancelPendingRead() => _decoratee.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            if (!_isCompleted)
            {
                _isCompleted = true;
                _decoratee.Complete(exception);
                _onCompleted();
            }
        }

        public override Task CopyToAsync(PipeWriter destination, CancellationToken cancellationToken = default) =>
            _decoratee.CopyToAsync(destination, cancellationToken);

        public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default) =>
            _decoratee.CopyToAsync(destination, cancellationToken);

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ReadAsync(cancellationToken);

        public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

        protected override ValueTask<ReadResult> ReadAtLeastAsyncCore(
            int minimumSize,
            CancellationToken cancellationToken) => _decoratee.ReadAtLeastAsync(minimumSize, cancellationToken);

        internal InputDecorator(PipeReader decoratee, Action onCompleted)
        {
            _decoratee = decoratee;
            _onCompleted = onCompleted;
        }
    }

    private class OutputDecorator : ReadOnlySequencePipeWriter
    {
        private readonly ReadOnlySequencePipeWriter _decoratee;
        private bool _isCompleted;
        private readonly Action _onCompleted;

        public override void Advance(int bytes) => _decoratee.Advance(bytes);

        // When leaveOpen is false, we use the default implementation of AsStream: we want the disposal of the stream
        // to call Complete on this decorator.
        public override Stream AsStream(bool leaveOpen = false) =>
            leaveOpen ? _decoratee.AsStream(leaveOpen) : base.AsStream(leaveOpen);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception = null)
        {
            if (!_isCompleted)
            {
                _isCompleted = true;
                _decoratee.Complete(exception);
                _onCompleted();
            }
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
            _decoratee.FlushAsync(cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default) => _decoratee.WriteAsync(source, cancellationToken);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken = default) =>
            _decoratee.WriteAsync(source, endStream, cancellationToken);

        internal OutputDecorator(ReadOnlySequencePipeWriter decoratee, Action onCompleted)
        {
            _decoratee = decoratee;
            _onCompleted = onCompleted;
        }
    }
}
