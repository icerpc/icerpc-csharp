// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class InvocationTests
{
    /// <summary>Verifies that a callback on a connection with no dispatcher throws DispatchException(ServiceNotFound)
    /// with the ice protocol.</summary>
    [Test]
    public async Task Bad_callback_throws_ServiceNotFound_with_ice()
    {
        // Arrange
        IInvoker? callbackInvoker = null;
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                (request, cancel) =>
                {
                    callbackInvoker = request.ConnectionContext.Invoker;
                    return new(new OutgoingResponse(request));
                }),
                Protocol.Ice)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(ServiceAddress.Parse("ice:/test"));
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);

        var callback = new OutgoingRequest(ServiceAddress.Parse("ice:/callback"));

        // Act
        IncomingResponse response = await callbackInvoker!.InvokeAsync(request);

        // Assert
        var exception = await response.DecodeFailureAsync(request, callbackInvoker);
        Assert.That(exception, Is.InstanceOf<DispatchException>());
        Assert.That(((DispatchException)exception).ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
    }

    /// <summary>Verifies that a callback on a connection with no dispatcher times out with the icerpc protocol.
    /// </summary>
    [Test]
    public async Task Connection_without_dispatcher_does_not_accept_requests_with_icerpc()
    {
        IInvoker? callbackInvoker = null;

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                (request, cancel) =>
                {
                    callbackInvoker = request.ConnectionContext.Invoker;
                    return new(new OutgoingResponse(request));
                }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(ServiceAddress.Parse("icerpc:/test"));
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);

        var callback = new OutgoingRequest(ServiceAddress.Parse("icerpc:/callback"));
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(
            async () => await callbackInvoker!.InvokeAsync(request, cancellationTokenSource.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
