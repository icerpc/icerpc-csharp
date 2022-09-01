// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class InvocationTests
{
    /// <summary>Verifies that a callback on a connection without a dispatcher throws DispatchException(ServiceNotFound)
    /// with the ice protocol.</summary>
    [Test]
    public async Task Connection_without_dispatcher_throws_ServiceNotFound_with_ice()
    {
        // Arrange
        IInvoker? callbackInvoker = null;
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                (request, cancellationToken) =>
                {
                    callbackInvoker = request.ConnectionContext.Invoker;
                    return new(new OutgoingResponse(request));
                }),
                Protocol.Ice)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("ice:/test")));
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);

        var callback = new OutgoingRequest(new ServiceAddress(new Uri("ice:/callback")));

        // Act
        IncomingResponse response = await callbackInvoker!.InvokeAsync(request);

        // Assert
        RemoteException exception = await response.DecodeFailureAsync(
            request,
            new ServiceProxy(callbackInvoker, callback.ServiceAddress));
        Assert.Multiple(
            () =>
            {
                Assert.That(exception, Is.InstanceOf<DispatchException>());
                Assert.That(((DispatchException)exception).ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
            });
    }

    /// <summary>Verifies that a callback on a connection without dispatcher does not accept requests with the icerpc
    /// protocol.</summary>
    [Test]
    public async Task Connection_without_dispatcher_does_not_accept_requests_with_icerpc()
    {
        // Arrange
        IInvoker? callbackInvoker = null;

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                (request, cancellationToken) =>
                {
                    callbackInvoker = request.ConnectionContext.Invoker;
                    return new(new OutgoingResponse(request));
                }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/test")));
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);

        var callback = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/callback")));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act and Assert
        Assert.That(
            async () => await callbackInvoker!.InvokeAsync(request, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Cancel_the_payload_reads_while_the_server_is_reading_the_arguments_fails_with_dispatch_exception()
    {
        var payload = new HoldPipeReader(new byte[] { 0x1, 0x2, 0x3 });

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                async (request, cancellationToken) =>
                {
                    await payload.ReadStart;
                    payload.CancelPendingRead();
                    await request.Payload.ReadAllAsync(CancellationToken.None);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/test")));
        request.Payload = payload;

        IncomingResponse responnse = await connection.InvokeAsync(request);

        RemoteException exception = await responnse.DecodeFailureAsync(request, new ServiceProxy());
        Assert.Multiple(
            () =>
            {
                Assert.That(responnse.ResultType, Is.EqualTo(ResultType.Failure));
                Assert.That(exception, Is.TypeOf<DispatchException>());
            });
    }

    private class HoldPipeReader : PipeReader
    {
        internal Task ReadStart => _readStartTcs.Task;

        private byte[] _initialData;

        private readonly TaskCompletionSource _readStartTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<ReadResult> _readTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead() =>
            _readTcs.SetResult(new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: true, isCompleted: false));

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            _readStartTcs.TrySetResult();

            if (_initialData.Length > 0)
            {
                var buffer = new ReadOnlySequence<byte>(_initialData);
                _initialData = Array.Empty<byte>();
                return new(new ReadResult(buffer, isCanceled: false, isCompleted: false));
            }
            else
            {
                // Hold until ReadAsync is canceled.
                return new(_readTcs.Task.WaitAsync(cancellationToken));
            }
        }

        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult();
            return false;
        }

        internal HoldPipeReader(byte[] initialData) => _initialData = initialData;

        internal void SetReadException(Exception exception) => _readTcs.SetException(exception);
    }
}
