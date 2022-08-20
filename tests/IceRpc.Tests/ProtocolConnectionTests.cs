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

    private static List<Protocol> Protocols => new() { Protocol.IceRpc, Protocol.Ice };

    private static IEnumerable<TestCaseData> Protocols_and_client_or_server
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Protocols_and_oneway_or_twoway
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    /// <summary>Verifies that the OnShutdown callback is called when idle.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
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

        await using ServiceProvider provider = services.BuildServiceProvider();

        TimeSpan? clientIdleCalledTime = null;
        TimeSpan? serverIdleCalledTime = null;

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        sut.Client.OnShutdown(_ => clientIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));
        sut.Server.OnShutdown(_ => serverIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64));

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
    [Test, TestCaseSource(nameof(Protocols))]
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
            .Configure(options => options.ConnectionOptions = new()
            {
                IdleTimeout = TimeSpan.FromMilliseconds(500),
                Dispatcher = ServiceNotFoundDispatcher.Instance
            });

        await using ServiceProvider provider = services.BuildServiceProvider();

        long startTime = Environment.TickCount64;
        long? clientIdleCalledTime = null;
        long? serverIdleCalledTime = null;

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        sut.Client.OnShutdown(_ => clientIdleCalledTime ??= Environment.TickCount64 - startTime);
        sut.Server.OnShutdown(_ => serverIdleCalledTime ??= Environment.TickCount64 - startTime);
        if (protocol == Protocol.Ice)
        {
            // TODO: with ice, the shutdown of the peer currently triggers an abort
            sut.Client.OnAbort(_ => clientIdleCalledTime ??= Environment.TickCount64 - startTime);
            sut.Server.OnAbort(_ => serverIdleCalledTime ??= Environment.TickCount64 - startTime);
        }

        var request = new OutgoingRequest(new ServiceAddress(protocol));
        IncomingResponse response = await sut.Client.InvokeAsync(request);
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

    /// <summary>Verifies that disposing the connection executes the OnAbort callback.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_abort_callback(Protocol protocol)
    {
        // Arrange
        var onAbortCalled = new TaskCompletionSource<Exception>();
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();

        // Initialize the connection.
        await sut.ConnectAsync();
        sut.Server.OnAbort(exception => onAbortCalled.SetException(exception));

        try
        {
            await sut.Client.ShutdownAsync("Triggering server abort", new CancellationToken(true));
        }
        catch (OperationCanceledException)
        {
        }

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(async () => await onAbortCalled.Task, Throws.InstanceOf<ConnectionLostException>());
    }

    /// <summary>Verifies that disposing the server connection cancels dispatches.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_cancels_dispatches(Protocol protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));

        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await dispatcher.DispatchComplete, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that disposing the client connection aborts pending invocations, the invocations will fail
    /// with <see cref="ObjectDisposedException"/>.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_aborts_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        Task invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionAbortedException>());
    }

    /// <summary>Ensures that the sending a request after shutdown fails.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Invoke_on_shutdown_connection_fails(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = sut.Client.ShutdownAsync("");

        // Act/Assert
        Assert.ThrowsAsync<ConnectionClosedException>(() => sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(protocol))));
    }

    /// <summary>Ensures that the request payload is completed on a valid request.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_oneway_or_twoway))]
    public async Task Payload_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on a valid response.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload writer.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task PayloadWriter_completed_with_valid_request(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new ServiceAddress(protocol));
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload writer is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);
    }

    [Test, TestCaseSource(nameof(Protocols))]
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

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        IncomingResponse response = await sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(protocol))
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            });

        // Assert
        ReadResult readResult = await response.Payload.ReadAllAsync(default);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(expectedPayload));
    }

    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new ServiceAddress(protocol))
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
        _ = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(field, Is.Not.Null);
            Assert.That(DecodeField(), Is.EqualTo(expectedValue));
        });

        Dictionary<string, string> DecodeField()
        {
            var decoder = new SliceDecoder(field, protocol.SliceEncoding);
            return decoder.DecodeDictionary(
                count => new Dictionary<string, string>(count),
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                (ref SliceDecoder decoder) => decoder.DecodeString());
        }
    }

    [Test, TestCaseSource(nameof(Protocols))]
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
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        await sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(protocol))
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            });

        // Assert
        Assert.That(receivedPayload, Is.EqualTo(expectedPayload));
    }

    /// <summary>Verifies connection shutdown is successful</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_connection(Protocol protocol, bool closeClientSide)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync("");

        // Assert
        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }

    /// <summary>Verifies that the connection shutdown waits for pending invocations and dispatches to finish.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_waits_for_pending_invocation_and_dispatch_to_finish(
        Protocol protocol,
        bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        Task invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task shutdownTask = (closeClientSide? sut.Client : sut.Server).ShutdownAsync("");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
            Assert.That(shutdownTask.IsCompleted, Is.False);
        });
        dispatcher.ReleaseDispatch();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await invokeTask, Throws.Nothing);
            Assert.That(async () => await shutdownTask, Throws.Nothing);
        });
    }

    /// <summary>Verifies that connect establishment timeouts after the <see cref="ConnectionOptions.ConnectTimeout"/>
    /// time period.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_timeout(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);
        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.ConnectTimeout = TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<TimeoutException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_connection_closed_connection_after_shutdown(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);
        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.ConnectTimeout = TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        await sut.Client.ShutdownAsync("");

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<ConnectionClosedException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_object_disposed_exception_after_dispose(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);
        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.ConnectTimeout = TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        await sut.Client.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_connection_closed_connection_after_shutdown_by_peer(Protocol protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol);
        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.ConnectTimeout = TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        await sut.Server.ShutdownAsync("");

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<ConnectionClosedException>());
    }

    /// <summary>Verifies that connection shutdown timeouts after the <see cref="ConnectionOptions.ShutdownTimeout"/>
    /// time period.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_timeout(Protocol protocol, bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol, dispatcher);
        if (closeClientSide)
        {
            services
                .AddOptions<ClientConnectionOptions>()
                .Configure(options => options.ShutdownTimeout = TimeSpan.FromSeconds(1));
        }
        else
        {
            services
                .AddOptions<ServerOptions>()
                .Configure(options => options.ConnectionOptions.ShutdownTimeout = TimeSpan.FromSeconds(1));
        }

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IClientServerProtocolConnection sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        Task invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol)));
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync("");

        // Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<TimeoutException>());
        if (closeClientSide)
        {
            await sut.Client.DisposeAsync();
            Assert.That(async () => await invokeTask, Throws.InstanceOf<ConnectionAbortedException>());
        }
        else
        {
            await sut.Server.DisposeAsync();
            Assert.That(async () => await invokeTask, Throws.InstanceOf<ConnectionLostException>());
        }
    }
}
