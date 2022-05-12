// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConnectionTests
{
    public enum ConnectionType
    {
        Client,
        Server
    }

    private static readonly List<Protocol> _protocols = new() { Protocol.Ice, Protocol.IceRpc };

    private static IEnumerable<TestCaseData> Payload_completed_on_request
    {
        get
        {
            foreach (Protocol protocol in _protocols)
            {
                yield return new TestCaseData(protocol);
            }
        }
    }

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
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();

        ClientServerProtocolConnection sut =
            await serviceProvider.GetClientServerProtocolConnectionAsync(acceptRequests: false);
        Task clientAcceptRequestsTask = sut.Client.AcceptRequestsAsync(serviceProvider.GetInvalidConnection());
        Task serverAcceptRequestsTask = sut.Server.AcceptRequestsAsync(serviceProvider.GetInvalidConnection());

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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

    /// <summary>Verifies that shutdown cancellation cancels shutdown even if a dispatch hangs.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Shutdown_completes_on_cancellation_and_dispatch_hang(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        // var dispatchCanceled = new TaskCompletionSource();

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(CancellationToken.None);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        IConnection connection = serviceProvider.GetInvalidConnection();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.AcceptRequestsAsync(connection);
        _ = sut.Server.AcceptRequestsAsync(connection);
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        // Act
        Task shutdownTask = sut.Server.ShutdownAsync("", cancel: cancellationTokenSource.Token);

        // Assert
        Assert.CatchAsync<OperationCanceledException>(() => shutdownTask);
        hold.Release();
    }

    /// <summary>Ensures that the connection HasInvocationInProgress works.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Connection_has_invocation_in_progress(Protocol protocol)
    {
        // Arrange
        var result = new TaskCompletionSource<bool>();
        ClientServerProtocolConnection? sut = null;
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher((request, cancel) =>
                {
                    result.SetResult(sut!.Value.Client.HasInvocationsInProgress);
                    return new(new OutgoingResponse(request));
                })
            })
            .BuildServiceProvider();
        sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher((request, cancel) =>
                {
                    result.SetResult(sut!.Value.Server.HasDispatchesInProgress);
                    return new(new OutgoingResponse(request));
                })
            })
            .BuildServiceProvider();
        sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);

                })
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        sut.Client.Dispose();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ObjectDisposedException>());

        hold.Release();
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Invoke_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();
        _ = sut.Server.AcceptRequestsAsync(connection);

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
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();
        _ = sut.Server.AcceptRequestsAsync(connection);

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();
        _ = sut.Server.AcceptRequestsAsync(connection);

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid request.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_request()
    {
        // Arrange
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Payload = InvalidPipeReader.Instance
        };
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request, InvalidConnection.IceRpc);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid response.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_response()
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            var response = new OutgoingResponse(request)
            {
                Payload = InvalidPipeReader.Instance
            };
            response.Use(writer =>
                {
                    var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                    payloadWriterSource.SetResult(payloadWriterDecorator);
                    return payloadWriterDecorator;
                });
            return new(response);
        });

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), InvalidConnection.IceRpc);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the PeerShutdownInitiated callback is called when the peer initiates the
    /// shutdown.</summary>
    [Test, TestCaseSource(nameof(Protocol_on_server_and_client_connection))]
    public async Task PeerShutdownInitiated_callback_is_called(Protocol protocol, ConnectionType connectionType)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection().UseProtocol(protocol).BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

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
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                    return new OutgoingResponse(request)
                    {
                        Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
                    };
                })
            })
            .BuildServiceProvider();

        IConnection connection = serviceProvider.GetInvalidConnection();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.AcceptRequestsAsync(connection);
        _ = sut.Server.AcceptRequestsAsync(connection);

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
        var expectedValue = new string('A', 4096);
        IDictionary<string, string>? context = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            context = request.Features.GetContext();
            return new(new OutgoingResponse(request));
        });
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(protocol))
        {
            Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = expectedValue })
        };

        // Act
        _ = await sut.Client.InvokeAsync(request, connection);

        // Assert
        Assert.That(context, Is.Not.Null);
        Assert.That(context!["foo"], Is.EqualTo(expectedValue));
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Send_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        byte[]? receivedPayload = null;
        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
                    receivedPayload = readResult.Buffer.ToArray();
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        IConnection connection = serviceProvider.GetInvalidConnection();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.AcceptRequestsAsync(connection);
        _ = sut.Server.AcceptRequestsAsync(connection);

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

        await using ServiceProvider serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(protocol)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();
        using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        IConnection connection = serviceProvider.GetInvalidConnection();

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
