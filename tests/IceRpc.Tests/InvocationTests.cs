// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
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
        // Arrange
        var dispatchStartTcs = new TaskCompletionSource();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0x1, 0x2, 0x3 }));
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                async (request, cancellationToken) =>
                {
                    dispatchStartTcs.SetResult();
                    await request.Payload.ReadAllAsync(cancellationToken);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/test")));
        request.Payload = pipe.Reader;
        var invokeTask = connection.InvokeAsync(request);
        await dispatchStartTcs.Task;

        // Act
        pipe.Reader.CancelPendingRead();

        // Assert
        var response = await invokeTask;
        RemoteException exception = await response.DecodeFailureAsync(request, new ServiceProxy());
        Assert.Multiple(
            () =>
            {
                Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
                Assert.That(exception, Is.TypeOf<DispatchException>());
                DispatchException dispatchException = (DispatchException)exception;
                Assert.That(dispatchException.ErrorCode, Is.EqualTo(DispatchErrorCode.UnhandledException));
            });
        await pipe.Writer.CompleteAsync();
    }
}
