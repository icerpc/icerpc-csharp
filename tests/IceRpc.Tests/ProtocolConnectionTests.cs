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

    private static readonly List<Protocol> _protocols = new() { Protocol.IceRpc, Protocol.Ice };

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

    /// <summary>Verifies that the OnShutdown callback is called when idle.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task OnShutdown_is_called_when_idle(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);

        services
            .AddOptions<ConnectionOptions>()
            .Configure(options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));
        services
            .AddOptions<ServerOptions>()
            .Configure(options => options.ConnectionOptions.IdleTimeout = TimeSpan.FromMilliseconds(500));

        await using var provider = services.BuildServiceProvider();

        TimeSpan? clientIdleCalledTime = null;
        TimeSpan? serverIdleCalledTime = null;

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        sut.Client.OnShutdown(_ => clientIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));
        sut.Server.OnShutdown(_ => serverIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));

        if (protocol == Protocol.Ice)
        {
            // TODO: the peer shutdown results in an abort with ice
            sut.Client.OnAbort(_ => clientIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));
            sut.Server.OnAbort(_ => serverIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));
        }

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(clientIdleCalledTime, Is.Not.Null);
            Assert.That(serverIdleCalledTime, Is.Not.Null);
            Assert.That(clientIdleCalledTime!.Value, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
            Assert.That(serverIdleCalledTime!.Value, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
        });
    }

    /// <summary>Verifies that the OnShutdown callback is called when idle and after the idle time has been
    /// deferred.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task OnShutdown_is_called_when_idle_and_idle_timeout_deferred(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);

        // TODO: why are we using the same timeout for the client and server? This results in a non-deterministic
        // behavior.
        services
            .AddOptions<ConnectionOptions>()
            .Configure(options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));
        services
            .AddOptions<ServerOptions>()
            .Configure(options => options.ConnectionOptions.IdleTimeout = TimeSpan.FromMilliseconds(500));

        await using var provider = services.BuildServiceProvider();

        long startTime = Environment.TickCount64;
        long? clientIdleCalledTime = null;
        long? serverIdleCalledTime = null;

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        sut.Client.OnShutdown(_ => clientIdleCalledTime ??= Environment.TickCount64 - startTime);
        sut.Server.OnShutdown(_ => serverIdleCalledTime ??= Environment.TickCount64 - startTime);
        if (protocol == Protocol.Ice)
        {
            // TODO: with ice, the shutdown of the peer currently triggers an abort
            sut.Client.OnAbort(_ => clientIdleCalledTime ??= Environment.TickCount64 - startTime);
            sut.Server.OnAbort(_ => serverIdleCalledTime ??= Environment.TickCount64 - startTime);
        }

        var request = new OutgoingRequest(new Proxy(protocol));
        IncomingResponse response = await sut.Client.InvokeAsync(request, InvalidConnection.ForProtocol(protocol));
        request.Complete();

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(
                TimeSpan.FromMilliseconds(clientIdleCalledTime!.Value),
                Is.GreaterThan(TimeSpan.FromMilliseconds(490)).And.LessThan(TimeSpan.FromSeconds(1)));
            Assert.That(
                TimeSpan.FromMilliseconds(serverIdleCalledTime!.Value),
                Is.GreaterThan(TimeSpan.FromMilliseconds(490)).And.LessThan(TimeSpan.FromSeconds(1)));
        });
    }

    /// <summary>Verifies that disposing a server connection aborts pending invocations, peer invocations will fail with
    /// <see cref="ConnectionLostException"/>.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Disposing_server_connection_aborts_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);

        });

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());

        hold.Release();
    }

    /// <summary>Verifies that disposing the client connection aborts pending invocations, the invocations will fail
    /// with <see cref="ObjectDisposedException"/>.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Disposing_client_connection_aborts_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionAbortedException>());

        hold.Release();
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Invoke_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = sut.Client.ShutdownAsync("");

        // Act/Assert
        Assert.ThrowsAsync<ConnectionClosedException>(() => sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(protocol)),
            connection));
    }

    /// <summary>Ensures that the request payload is completed on a valid request.</summary>
    [Test, TestCaseSource(nameof(Payload_completed_on_twoway_and_oneway_request))]
    public async Task Payload_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(protocol)), connection);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Receive_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
            request.Payload.AdvanceTo(readResult.Buffer.End);
            return new OutgoingResponse(request)
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            };
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        var response = await sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(protocol))
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            },
            connection,
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
        var expectedValue = new Dictionary<string, string>
        {
            ["ctx"] = new string('C', 4096)
        };
        byte[]? field = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            field = request.Fields[RequestFieldKey.Context].ToArray();
            return new(new OutgoingResponse(request));
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new Proxy(protocol))
        {
            Fields = new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [RequestFieldKey.Context] = new OutgoingFieldValue(
                    (ref SliceEncoder encoder) => encoder.EncodeDictionary(
                        expectedValue,
                        (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                        (ref SliceEncoder encoder, string value) => encoder.EncodeString(value)))
            }
        };

        // Act
        _ = await sut.Client.InvokeAsync(request, connection);

        // Assert
        Assert.That(field, Is.Not.Null);
        Assert.That(DecodeField(), Is.EqualTo(expectedValue));

        Dictionary<string, string> DecodeField()
        {
            var decoder = new SliceDecoder(field, protocol.SliceEncoding);
            return decoder.DecodeDictionary(
                count => new Dictionary<string, string>(count),
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                (ref SliceDecoder decoder) => decoder.DecodeString());
        }
    }

    [Test, TestCaseSource(nameof(_protocols))]
    public async Task Send_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        byte[]? receivedPayload = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            ReadResult readResult = await request.Payload.ReadAllAsync(cancel);
            receivedPayload = readResult.Buffer.ToArray();
            request.Payload.AdvanceTo(readResult.Buffer.End);
            return new OutgoingResponse(request);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IConnection connection = provider.GetRequiredService<IConnection>();
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

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
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        IConnection connection = provider.GetRequiredService<IConnection>();
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

    /// <summary>Verifies that the connection shutdown waits for invocations to finish.</summary>
    [Test]
    public async Task Shutdown_waits_for_pending_invocations_to_finish()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.IceRpc)),
            InvalidConnection.IceRpc);

        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        var shutdownTask = sut.Client.ShutdownAsync("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
            Assert.That(shutdownTask.IsCompleted, Is.False);
        });
        hold.Release();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await invokeTask, Throws.Nothing);
            Assert.That(async () => await shutdownTask, Throws.Nothing);
        });
    }
}
