// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class IceProtocolConnectionTests
{
    /// <summary>Ensures that the request payload stream is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadStream_completed_on_request()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.Ice))
        {
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);
        bool payloadCompleted = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(payloadCompleted, Is.True);
            Assert.That(payloadStreamDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
        });
    }

    /// <summary>Ensures that the response payload stream is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadStream_completed_on_response()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetProtocolConnectionPairAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)));
        bool completedCalled = await payloadStreamDecorator.CompleteCalled;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(completedCalled, Is.True);
            Assert.That(payloadStreamDecorator.CompleteException, Is.InstanceOf<NotSupportedException>());
        });
    }
}
