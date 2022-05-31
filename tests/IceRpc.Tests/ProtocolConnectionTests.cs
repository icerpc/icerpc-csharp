// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConnectionTests
{
    public enum ConnectionType
    {
        Client,
        Server
    }

    private static readonly List<Protocol> _protocols = new() { Protocol.IceRpc };

    private static IEnumerable<TestCaseData> Payload_completed_on_twoway_and_oneway_request
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Protocol_on_server_and_client_connection
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol, ConnectionType.Client);
                yield return new TestCaseData(protocol, ConnectionType.Server);
            }
        }
    }

    /// <summary>Ensures that AcceptRequestsAsync returns successfully when the connection is gracefully
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task AcceptRequests_returns_successfully_on_graceful_shutdown(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut =
            await serviceProvider.GetClientServerProtocolConnectionAsync(protocol, acceptRequests: false);
        Task clientAcceptRequestsTask = sut.Client.AcceptRequestsAsync(serviceProvider.GetRequiredService<IConnection>());
        Task serverAcceptRequestsTask = sut.Server.AcceptRequestsAsync(serviceProvider.GetRequiredService<IConnection>());

        // Act
        _ = sut.Client.ShutdownAsync("");
        _ = sut.Server.ShutdownAsync("");

        // Assert
        Assert.DoesNotThrowAsync(() => clientAcceptRequestsTask);
        Assert.DoesNotThrowAsync(() => serverAcceptRequestsTask);
    }

    /// <summary>Verifies that calling ShutdownAsync with a canceled token results in the cancellation of the the
    /// pending dispatches.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Shutdown_dispatch_cancellation(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        sut.Client.PeerShutdownInitiated += (message) => sut.Client.ShutdownAsync("shutdown", default);
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        Task shutdownTask = sut.Server.ShutdownAsync("", new CancellationToken(canceled: true));

        // Assert
        Exception? ex = Assert.CatchAsync(async () =>
        {
            IncomingResponse response = await invokeTask;
            DecodeAndThrowException(response);
        });
        Assert.Multiple(() =>
        {
            Assert.That(ex!, Is.TypeOf<OperationCanceledException>());
            Assert.That(async () => await shutdownTask, Throws.Nothing);
        });

        // TODO should we raise OperationCanceledException directly from Ice, here with Ice we get a DispatchException
        // with DispatchErrorCode.Canceled and with IceRpc we get OperationCanceledException
        static void DecodeAndThrowException(IncomingResponse response)
        {
            if (response.Payload.TryRead(out ReadResult readResult))
            {
                var decoder = new SliceDecoder(readResult.Buffer, response.Protocol.SliceEncoding);
                DispatchException dispatchException = decoder.DecodeSystemException();
                if (dispatchException.ErrorCode == DispatchErrorCode.Canceled)
                {
                    throw new OperationCanceledException();
                }
                throw dispatchException;
            }
        }
    }

    /// <summary>Ensures that the connection HasInvocationInProgress works.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_invocation_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        ClientServerProtocolConnection? sut = null;
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher((request, cancel) =>
                {
                    result.SetResult(sut!.Value.Client.HasInvocationsInProgress);
                    return new(new OutgoingResponse(request));
                }))
            .BuildServiceProvider(validateScopes: true);
        sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        await sut.Value.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await result.Task, Is.True);
        sut!.Value.Dispose();
    }

    /// <summary>Ensures that the connection HasDispatchInProgress works.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_dispatch_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        ClientServerProtocolConnection? sut = null;
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher((request, cancel) =>
                {
                    result.SetResult(sut!.Value.Server.HasDispatchesInProgress);
                    return new(new OutgoingResponse(request));
                }))
            .BuildServiceProvider(validateScopes: true);
        sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        await sut.Value.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await result.Task, Is.True);
        sut!.Value.Dispose();
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Dispose_the_protocol_connections(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);

        // Act
        sut.Client.Dispose();
        sut.Server.Dispose();
    }

    /// <summary>Verifies that disposing the server connection kills pending invocations, peer invocations will fail
    /// with <see cref="ConnectionLostException"/>.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Dispose_server_connection_kills_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);

                }))
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        sut.Server.Dispose();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());
        hold.Release();
    }

    /// <summary>Verifies that disposing the client connection kills pending invocations, the invocations will fail
    /// with <see cref="ObjectDisposedException"/>.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Dispose_client_connection_kills_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        sut.Client.Dispose();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionClosedException>());

        hold.Release();
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Invoke_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        _ = sut.Client.ShutdownAsync("");

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(protocol)),
            connection);

        // Assert
        Assert.ThrowsAsync<ConnectionClosedException>(async () => await invokeTask);
    }

    /// <summary>Ensures that the request payload is completed on a valid request.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task Payload_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request, connection);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on a valid response.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_invalid_response_payload(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload writer.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Payload_completed_on_invalid_response_payload_writer(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                };
                response.Use(writer => InvalidPipeWriter.Instance);
                return new(response);
            });

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task PayloadWriter_completed_with_valid_request(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var request = new OutgoingRequest(new Proxy(protocol));
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request, connection);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload writer is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task PayloadWriter_completed_with_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request);
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    /// <summary>Ensures that the PeerShutdownInitiated callback is called when the peer initiates the
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(Protocol_on_server_and_client_connection))]
    public async Task PeerShutdownInitiated_callback_is_called(Protocol protocol, ConnectionType connectionType)
    {
        // Arrange
        await using var serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);

        IProtocolConnection connection1 = connectionType == ConnectionType.Client ? sut.Server : sut.Client;
        IProtocolConnection connection2 = connectionType == ConnectionType.Client ? sut.Client : sut.Server;

        var shutdownInitiatedCalled = new TaskCompletionSource<string>();
        connection2.PeerShutdownInitiated = message =>
        {
            shutdownInitiatedCalled.SetResult(message);
            _ = connection2.ShutdownAsync("");
        };

        // Act
        _ = connection1.ShutdownAsync("hello world");

        // Assert
        string message = protocol == Protocol.Ice ? "connection shutdown by peer" : "hello world";
        Assert.That(await shutdownInitiatedCalled.Task, Is.EqualTo(message));
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Receive_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                    return new OutgoingResponse(request)
                    {
                        Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
                    };
                }))
            .BuildServiceProvider(validateScopes: true);

        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);

        // Act
        var response = await sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(protocol))
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            },
            protocol == Protocol.IceRpc ? InvalidConnection.IceRpc : InvalidConnection.Ice,
            default);

        // Assert
        var readResult = await response.Payload.ReadAllAsync(default);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(expectedPayload));
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Request_with_large_header(Protocol protocol)
    {
        // Arrange
        // This large value should be large enough to create multiple buffers for the request header.
        var expectedValue = Enumerable.Range(0, 4096).Select(value => (byte)value).ToArray();
        byte[]? field = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            field = request.Fields[(RequestFieldKey)1024].ToArray();
            return new(new OutgoingResponse(request));
        });
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            Fields = new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [(RequestFieldKey)1024] = new OutgoingFieldValue(new ReadOnlySequence<byte>(expectedValue))
            }
        };

        // Act
        _ = await sut.Client.InvokeAsync(request, connection);

        // Assert
        Assert.That(field, Is.Not.Null);
        Assert.That(field, Is.EqualTo(expectedValue));
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Send_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        byte[]? receivedPayload = null;
        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                    receivedPayload = readResult.Buffer.ToArray();
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);

        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);

        // Act
        await sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(protocol))
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            },
            protocol == Protocol.IceRpc ? InvalidConnection.IceRpc : InvalidConnection.Ice,
            default);

        // Assert
        Assert.That(receivedPayload, Is.EqualTo(expectedPayload));
    }

    /// <summary>Verifies that a connection will not accept further request after shutdown was called, and it will
    /// allow pending dispatches to finish.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Shutdown_prevents_accepting_new_requests_and_let_pending_dispatches_complete(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using ServiceProvider serviceProvider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync(protocol);
        IConnection connection = serviceProvider.GetRequiredService<IConnection>();

        sut.Client.PeerShutdownInitiated = message => _ = sut.Client.ShutdownAsync(message);
        var invokeTask1 = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        var shutdownTask = sut.Server.ShutdownAsync("");

        // Assert
        var invokeTask2 = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        hold.Release();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await invokeTask1, Throws.Nothing);
            Assert.That(async () => await invokeTask2, Throws.TypeOf<ConnectionClosedException>());
            Assert.That(async () => await shutdownTask, Throws.Nothing);
        });
    }
}
