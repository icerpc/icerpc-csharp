// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class IceRpcProtocolConnectionTests
{
    public static IEnumerable<TestCaseData> ExceptionIsEncodedAsDispatchExceptionSource
    {
        get
        {
            yield return new TestCaseData(new InvalidDataException("invalid data"), StatusCode.InvalidData);
            // Slice1 only exception will get encoded as unhandled exception with Slice2
            yield return new TestCaseData(new MyDerivedException(), StatusCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), StatusCode.UnhandledException);
        }
    }

    /// <summary>This test ensures that disposing the connection correctly aborts the incoming request underlying
    /// stream.</summary>
    [Test]
    public async Task Disposing_connection_aborts_non_completed_incoming_request_stream()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var outgoingRequest = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[10]);
        outgoingRequest.PayloadContinuation = pipe.Reader;

        var invokeTask = sut.Client.InvokeAsync(outgoingRequest);
        IncomingRequest incomingRequest = await dispatcher.DispatchStart; // Wait for the dispatch to start
        // Make sure the payload continuation isn't completed by the dispatch termination.
        var payload = incomingRequest.DetachPayload();
        dispatcher.ReleaseDispatch();
        await invokeTask;
        ReadResult readResult = await payload.ReadAtLeastAsync(10); // Read everything
        payload.AdvanceTo(readResult.Buffer.End);

        try
        {
            await sut.Server.ShutdownAsync(new CancellationToken(true));
        }
        catch (OperationCanceledException)
        {
        }

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        // TODO: we get ConnectionDisposed with Slic and ConnectionReset with Quic
        Assert.That(async () => await payload.ReadAsync(), Throws.InstanceOf<TransportException>()
            .With.Property("ErrorCode").EqualTo(TransportErrorCode.ConnectionDisposed)
            .Or.With.Property("ErrorCode").EqualTo(TransportErrorCode.ConnectionReset));

        payload.Complete();
        pipe.Writer.Complete();
    }

    [Test]
    [Ignore("See #1859")]
    public async Task Close_server_multiplexed_connection_before_connect()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        await using var clientConnection = new IceRpcProtocolConnection(
            clientTransport.CreateConnection(
                listener.ServerAddress,
                options: provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                clientAuthenticationOptions: provider.GetService<SslClientAuthenticationOptions>()),
            transportConnectionInformation: null,
            options: new(),
            NullLogger.Instance);

        IMultiplexedConnection? serverConnection = null;
        Task acceptTask = AcceptAndShutdownAsync();

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            async () => await clientConnection.ConnectAsync(default));
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ConnectRefused));

        // Cleanup
        await serverConnection!.DisposeAsync();

        async Task AcceptAndShutdownAsync()
        {
            serverConnection = (await listener.AcceptAsync(default)).Connection;
            await serverConnection.CloseAsync((ulong)IceRpcConnectionErrorCode.Refused, default).ConfigureAwait(false);
        }
    }

    /// <summary>Verifies that disposing a server connection causes the invocation to fail with <see
    /// cref="TruncatedDataException" />.</summary>
    [Test]
    public async Task Disposing_server_connection_triggers_payload_read_with_truncated_data(
        [Values(false, true)] bool shutdown)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        if (shutdown)
        {
            _ = sut.Server.ShutdownAsync();
        }
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.InstanceOf<TruncatedDataException>());
    }

    [Test]
    public async Task Invocation_cancellation_triggers_dispatch_cancellation()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        using var cts = new CancellationTokenSource();
        Task invokeTask = sut.Client.InvokeAsync(request, cts.Token);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await dispatcher.DispatchComplete, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that canceling the invocation while sending the request payload, completes the incoming
    /// request payload with a <see cref="TruncatedDataException"/>.</summary>
    [Test]
    public async Task Invocation_cancellation_while_sending_the_payload_completes_the_input_request_payload()
    {
        // Arrange
        var dispatcher = new ConsumePayloadDispatcher(returnResponseFirst: false);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[10]));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Payload = pipe.Reader
        };
        using var invocationCts = new CancellationTokenSource();
        Task invokeTask = sut.Client.InvokeAsync(request, invocationCts.Token);
        await dispatcher.PayloadReadStarted;

        // Act
        invocationCts.Cancel();

        // Assert
        Assert.That(async () => await dispatcher.PayloadReadCompleted, Throws.TypeOf<TruncatedDataException>());
        Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that canceling the invocation while sending the request payload continuation, completes the
    /// incoming request payload with a <see cref="TruncatedDataException"/>.</summary>
    [Test]
    public async Task Invocation_cancellation_while_sending_the_payload_continuation_completes_the_input_request_payload()
    {
        // Arrange
        var dispatcher = new ConsumePayloadDispatcher(returnResponseFirst: false);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[10]));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Payload = EmptyPipeReader.Instance,
            PayloadContinuation = pipe.Reader
        };
        using var invocationCts = new CancellationTokenSource();
        Task invokeTask = sut.Client.InvokeAsync(request, invocationCts.Token);
        await dispatcher.PayloadReadStarted;

        // Act
        invocationCts.Cancel();

        // Assert
        Assert.That(async () => await dispatcher.PayloadReadCompleted, Throws.TypeOf<TruncatedDataException>());
        Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that canceling the invocation after receiving a response doesn't affect the reading of the
    /// payload.</summary>
    [Test]
    public async Task Invocation_cancellation_after_receive_response_doesnt_complete_the_incoming_request_payload()
    {
        // Arrange
        var dispatcher = new ConsumePayloadDispatcher(returnResponseFirst: true);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[10]));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Payload = EmptyPipeReader.Instance,
            PayloadContinuation = pipe.Reader,
        };
        using var cts = new CancellationTokenSource();
        _ = await sut.Client.InvokeAsync(request, cts.Token);

        // Act
        cts.Cancel();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[10]));
        pipe.Writer.Complete();

        // Assert
        Assert.That(async () => await dispatcher.PayloadReadCompleted, Throws.Nothing);
    }

    [Test]
    public async Task Invocation_stream_failure_triggers_incoming_request_truncated_data_exception()
    {
        // Arrange
        var remotePayloadTcs = new TaskCompletionSource<PipeReader>();
        var dispatcher = new InlineDispatcher(
            (request, cancellationToken) =>
            {
                remotePayloadTcs.SetResult(request.DetachPayload());
                return new(new OutgoingResponse(request));
            });

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        // Use initial payload data to ensure the request is sent before the payload reader blocks (Slic sends the
        // request header with the start of the payload so if the first ReadAsync blocks, the request header is not
        // sent).
        var payloadContinuation = new HoldPipeReader(new byte[10]);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            PayloadContinuation = payloadContinuation
        };
        await sut.Client.InvokeAsync(request);
        PipeReader remotePayload = await remotePayloadTcs.Task;

        // Act
        payloadContinuation.SetReadException(new OperationCanceledException()); // can be any exception

        // Assert
        Assert.That(
            async () =>
            {
                // The failure to read the remote payload is timing dependent. ReadAsync might return with the 10 bytes
                // initial payload and then fail or directly fail.

                ReadResult result = await remotePayload.ReadAsync();
                Assert.That(result.IsCompleted, Is.False);
                Assert.That(result.Buffer, Has.Length.EqualTo(10));

                remotePayload.AdvanceTo(result.Buffer.End);
                await remotePayload.ReadAsync();
            },
            Throws.InstanceOf<TruncatedDataException>());
    }

    [Test]
    public async Task Not_dispatched_twoway_request_gets_connection_exception_on_server_connection_shutdown()
    {
        // Arrange
        HoldMultiplexedServerTransport? serverTransport = null;
        using var dispatcher = new TestDispatcher();

        await using var provider = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddSingleton<IMultiplexedServerTransport>(
                provider => serverTransport = new HoldMultiplexedServerTransport(
                    new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>())))
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://colochost"))
            .AddIceRpcProtocolTest(
                clientConnectionOptions: new(),
                serverConnectionOptions: new() { Dispatcher = dispatcher })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask = sut.Client.InvokeAsync(request1);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        serverTransport!.Hold(HoldOperation.AcceptStream); // Don't accept the next stream.
        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask2 = sut.Client.InvokeAsync(request2);

        // Act
        _ = sut.Server.ShutdownAsync();

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await invokeTask2);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByPeer));
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask, Throws.Nothing);
    }

    [Test]
    public async Task Not_sent_request_gets_connection_exception_on_server_connection_shutdown(
        [Values(false, true)] bool isOneway)
    {
        // Arrange
        HoldMultiplexedClientTransport? clientTransport = null;
        using var dispatcher = new TestDispatcher();

        await using var provider = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddSingleton<IMultiplexedClientTransport>(
                provider => clientTransport = new HoldMultiplexedClientTransport(
                    new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>())))
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://colochost"))
            .AddIceRpcProtocolTest(
                clientConnectionOptions: new(),
                serverConnectionOptions: new() { Dispatcher = dispatcher })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask = sut.Client.InvokeAsync(request1);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        clientTransport!.Hold(HoldOperation.Write); // Hold writes to prevent the sending of the request.
        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)) { IsOneway = isOneway };
        var invokeTask2 = sut.Client.InvokeAsync(request2);

        // Act
        _ = sut.Server.ShutdownAsync();

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await invokeTask2);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByPeer));
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask, Throws.Nothing);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_response_payload()
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<TruncatedDataException>());
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload writer.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_response_payload_writer()
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            var response = new OutgoingResponse(request)
            {
                Payload = payloadDecorator
            };
            response.Use(writer => InvalidPipeWriter.Instance);
            return new(response);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<TruncatedDataException>());
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
    }

    /// <summary>Ensures that the response payload is completed if the response fields are invalid.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_response_fields()
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                };
                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));
                return new(response);
            });

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        Assert.That(async () => await responseTask, Throws.InstanceOf<TruncatedDataException>());
    }

    /// <summary>Ensures that the payload continuation of a request is completed when the dispatcher does not read this
    /// PipeReader.</summary>
    [Test]
    public async Task PayloadContinuation_of_outgoing_request_completed_when_not_read_by_dispatcher(
        [Values] bool isOneway)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, response) => new(new OutgoingResponse(request)));
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[10]);
        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(pipe.Reader);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadContinuation = payloadContinuationDecorator
        };

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(async () => await payloadContinuationDecorator.Completed, Is.Null);

        // Cleanup
        pipe.Writer.Complete();
        await responseTask;
    }

    /// <summary>Ensures that the payload continuation of a request is completed when it reaches the endStream.</summary>
    [Test]
    public async Task PayloadContinuation_of_outgoing_request_completed_on_end_stream(
        [Values(false, true)] bool isOneway)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadContinuation = payloadContinuationDecorator
        };

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart;

        // Assert
        Assert.That(async () => await payloadContinuationDecorator.Completed, Is.Null);
        if (!isOneway)
        {
            Assert.That(invokeTask.IsCompleted, Is.False);
        }

        // Cleanup
        dispatcher.ReleaseDispatch();
        await invokeTask;
    }

    /// <summary>Ensures that the request payload continuation is completed if the payload continuation is invalid.
    /// </summary>
    [Test]
    public async Task Payload_completed_on_invalid_request_payload_continuation([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadContinuation = payloadContinuationDecorator
        };

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadContinuationDecorator.Completed, Is.Null);

        // Cleanup
        try
        {
            await responseTask;
        }
        catch (TruncatedDataException)
        {
            // Depending on the timing, the payload stream send failure might abort the invocation before the response
            // is sent.
        }
    }

    /// <summary>Ensures that the response payload continuation is completed on a valid response.</summary>
    [Test]
    public async Task PayloadContinuation_completed_on_valid_response()
    {
        // Arrange
        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    PayloadContinuation = payloadContinuationDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadContinuationDecorator.Completed, Is.Null);

        // Cleanup
        await responseTask;
    }

    /// <summary>Ensures that the response payload continuation is completed on an invalid response payload
    /// continuation.</summary>
    [Test]
    public async Task PayloadContinuation_completed_on_invalid_response_payload_continuation()
    {
        // Arrange
        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    PayloadContinuation = payloadContinuationDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadContinuationDecorator.Completed, Is.Null);

        // Cleanup
        try
        {
            await responseTask;
        }
        catch (TruncatedDataException)
        {
            // Depending on the timing, the payload stream send failure might abort the invocation before the response
            // is sent.
        }
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid request.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_request()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
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
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Not.Null); // actual exception does not matter
        Assert.That(async () => await responseTask, Throws.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid response.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_response()
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
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

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Not.Null);
        Assert.That(async () => await responseTask, Throws.InstanceOf<TruncatedDataException>());
    }

    [Test]
    public async Task Request_with_header_size_larger_than_max_header_size_fails()
    {
        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                serverConnectionOptions: new ConnectionOptions { MaxIceRpcHeaderSize = 100 })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Operation = new string('x', 100)
        };

        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<ProtocolException>());
    }

    [Test]
    public async Task Response_with_large_header()
    {
        // Arrange
        // This large value should be large enough to create multiple buffers for the response header.
        var expectedValue = new string('A', 16_000);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            var response = new OutgoingResponse(request);
            response.Fields = response.Fields.With(
                (ResponseFieldKey)1000,
                (ref SliceEncoder encoder) => encoder.EncodeString(expectedValue));
            return new(response);
        });
        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(
            response.Fields.DecodeValue((ResponseFieldKey)1000, (ref SliceDecoder decoder) => decoder.DecodeString()),
            Is.EqualTo(expectedValue));
    }

    [Flags]
    private enum HoldOperation
    {
        None,
        AcceptStream,
        Write,
    }

    private sealed class HoldMultiplexedServerTransport : IMultiplexedServerTransport, IAsyncDisposable
    {
        private readonly IMultiplexedServerTransport _decoratee;
        private HoldMultiplexedListener? _listener;

        public string Name => _decoratee.Name;

        public ValueTask DisposeAsync() => _listener?.DisposeAsync() ?? new();

        public IListener<IMultiplexedConnection> Listen(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslServerAuthenticationOptions? serverAuthenticationOptions) =>
            _listener = new HoldMultiplexedListener(
                _decoratee.Listen(serverAddress, options, serverAuthenticationOptions));

        internal HoldMultiplexedServerTransport(IMultiplexedServerTransport decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _listener?.Hold(operation);
    }

    private sealed class HoldMultiplexedClientTransport : IMultiplexedClientTransport, IAsyncDisposable
    {
        private readonly IMultiplexedClientTransport _decoratee;
        private HoldMultiplexedConnection? _connection;

        public string Name => _decoratee.Name;

        public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

        public IMultiplexedConnection CreateConnection(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslClientAuthenticationOptions? clientAuthenticationOptions)
        {
            Debug.Assert(_connection is null);
            _connection = new HoldMultiplexedConnection(
                _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions));
            return _connection;
        }

        public ValueTask DisposeAsync() => _connection?.DisposeAsync() ?? new();

        internal HoldMultiplexedClientTransport(IMultiplexedClientTransport decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _connection!.Hold(operation);
    }

    private sealed class HoldMultiplexedListener : IListener<IMultiplexedConnection>
    {
        private readonly IListener<IMultiplexedConnection> _decoratee;
        private HoldMultiplexedConnection? _connection;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_connection is null);
            (IMultiplexedConnection connection, EndPoint address) = await _decoratee.AcceptAsync(cancellationToken);
            _connection = new HoldMultiplexedConnection(connection!);
            return (_connection, address);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection!.DisposeAsync();
            await _decoratee.DisposeAsync();
        }

        internal HoldMultiplexedListener(IListener<IMultiplexedConnection> decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => _connection!.Hold(operation);
    }

    private sealed class HoldMultiplexedConnection : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;
        internal HoldOperation HoldOperation { get; private set; }

        private readonly IMultiplexedConnection _decoratee;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
        {
            var stream = new HoldMultiplexedStream(this, await _decoratee.AcceptStreamAsync(cancellationToken));
            if (HoldOperation.HasFlag(HoldOperation.AcceptStream))
            {
                await Task.Delay(-1, cancellationToken);
            }
            return stream;
        }

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            _decoratee.ConnectAsync(cancellationToken);

        public async ValueTask<IMultiplexedStream> CreateStreamAsync(
            bool bidirectional,
            CancellationToken cancellationToken) =>
            new HoldMultiplexedStream(
                this,
                await _decoratee.CreateStreamAsync(bidirectional, cancellationToken).ConfigureAwait(false));

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        public Task CloseAsync(ulong errorCode, CancellationToken cancellationToken) =>
            _decoratee.CloseAsync(errorCode, cancellationToken);

        internal HoldMultiplexedConnection(IMultiplexedConnection decoratee) => _decoratee = decoratee;

        internal void Hold(HoldOperation operation) => HoldOperation |= operation;
    }

    private sealed class HoldMultiplexedStream : IMultiplexedStream
    {
        public ulong Id => _decoratee.Id;

        public PipeReader Input => _decoratee.Input;

        public bool IsBidirectional => _decoratee.IsBidirectional;

        public bool IsRemote => _decoratee.IsRemote;

        public bool IsStarted => _decoratee.IsStarted;

        public PipeWriter Output => _output ?? _decoratee.Output;

        public Task InputClosed => _decoratee.InputClosed;

        public Task OutputClosed => _decoratee.OutputClosed;

        public HoldOperation HoldOperation =>
            // Don't hold operations for the icerpc control stream
            (IsBidirectional || !IsStarted || Id >= 4) ? _connection.HoldOperation : HoldOperation.None;

        private readonly HoldMultiplexedConnection _connection;
        private readonly IMultiplexedStream _decoratee;
        private readonly HoldPipeWriter? _output;

        internal HoldMultiplexedStream(HoldMultiplexedConnection connection, IMultiplexedStream decoratee)
        {
            _connection = connection;
            _decoratee = decoratee;
            if (!IsRemote || IsBidirectional)
            {
                _output = new HoldPipeWriter(this, decoratee.Output);
            }
        }
    }

    private class HoldPipeWriter : PipeWriter
    {
        private readonly HoldMultiplexedStream _stream;
        private readonly PipeWriter _decoratee;

        public override void Advance(int bytes) => _decoratee.Advance(bytes);

        public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

        public override void Complete(Exception? exception) => _decoratee.Complete(exception);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            if (_stream.HoldOperation.HasFlag(HoldOperation.Write))
            {
                await Task.Delay(-1, cancellationToken);
            }
            return await _decoratee.FlushAsync(cancellationToken);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            if (_stream.HoldOperation.HasFlag(HoldOperation.Write))
            {
                await Task.Delay(-1, cancellationToken);
            }
            return await _decoratee.WriteAsync(source, cancellationToken);
        }

        internal HoldPipeWriter(HoldMultiplexedStream stream, PipeWriter decoratee)
        {
            _stream = stream;
            _decoratee = decoratee;
        }
    }

    private class HoldPipeReader : PipeReader
    {
        private byte[] _initialData;

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

    /// <summary>A dispatcher that reads the request payload, the <see cref="PayloadReadStarted"/> task is completed
    /// after start reading the payload, and the <see cref="PayloadReadCompleted"/> task is completed after reading
    /// the full payload.</summary>
    public sealed class ConsumePayloadDispatcher : IDispatcher
    {
        public Task PayloadReadCompleted => _completeTaskCompletionSource.Task;
        public Task PayloadReadStarted => _startTaskCompletionSource.Task;

        private readonly TaskCompletionSource _completeTaskCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly bool _returnResponseFirst;

        private readonly TaskCompletionSource _startTaskCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Constructs a ConsumePayloadDispatcher dispatcher.</summary>
        /// <param name="returnResponseFirst">whether to return a response before reading the payload.</param>
        public ConsumePayloadDispatcher(bool returnResponseFirst) => _returnResponseFirst = returnResponseFirst;

        public async ValueTask<OutgoingResponse> DispatchAsync(
            IncomingRequest request,
            CancellationToken cancellationToken)
        {
            ReadResult result = await request.Payload.ReadAsync(CancellationToken.None);
            request.Payload.AdvanceTo(result.Buffer.End);
            _startTaskCompletionSource.TrySetResult();
            if (_returnResponseFirst)
            {
                PipeReader payload = request.DetachPayload();
                _ = ReadFullPayloadAsync(payload);
            }
            else
            {
                await ReadFullPayloadAsync(request.Payload);
            }
            return new OutgoingResponse(request);

            async Task ReadFullPayloadAsync(PipeReader payload)
            {
                try
                {
                    ReadResult result = default;
                    do
                    {
                        result = await payload.ReadAsync(CancellationToken.None);
                        payload.AdvanceTo(result.Buffer.End);
                    }
                    while (!result.IsCompleted && !result.IsCanceled);
                    _completeTaskCompletionSource.TrySetResult();
                }
                catch (Exception exception)
                {
                    _completeTaskCompletionSource.TrySetException(exception);
                    throw;
                }
                finally
                {
                    if (_returnResponseFirst)
                    {
                        // We've detached the payload so we need to complete it.
                        payload.Complete();
                    }
                }
            }
        }
    }

}
