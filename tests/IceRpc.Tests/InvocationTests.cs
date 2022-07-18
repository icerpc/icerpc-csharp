// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

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
                (request, cancel) =>
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
        var exception = await response.DecodeFailureAsync(request, callbackInvoker);
        Assert.That(exception, Is.InstanceOf<DispatchException>());
        Assert.That(((DispatchException)exception).ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
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
                (request, cancel) =>
                {
                    callbackInvoker = request.ConnectionContext.Invoker;
                    return new(new OutgoingResponse(request));
                }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/test")));
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);

        var callback = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/callback")));
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act and Assert
        Assert.That(
            async () => await callbackInvoker!.InvokeAsync(request, cancellationTokenSource.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
