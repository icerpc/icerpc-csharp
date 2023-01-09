// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
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
    private static IEnumerable<TestCaseData> DispatchExceptionSource
    {
        get
        {
            var invalidDataException = new InvalidDataException("invalid data");
            yield return new TestCaseData(
                new InvalidDataException("invalid data"),
                StatusCode.InvalidData,
                GetErrorMessage(StatusCode.InvalidData, invalidDataException));

            var invalidOperationException = new InvalidOperationException("invalid op message");
            yield return new TestCaseData(
                invalidOperationException,
                StatusCode.UnhandledException,
                GetErrorMessage(StatusCode.UnhandledException, invalidOperationException));

            var applicationError = new DispatchException(StatusCode.ApplicationError, "application message");
            yield return new TestCaseData(
                applicationError,
                applicationError.StatusCode,
                applicationError.Message);

            var deadlineExpired = new DispatchException(StatusCode.DeadlineExpired, "deadline message");
            yield return new TestCaseData(
                deadlineExpired,
                deadlineExpired.StatusCode,
                deadlineExpired.Message);

            var serviceNotFound = new DispatchException(StatusCode.ServiceNotFound);
            yield return new TestCaseData(
                serviceNotFound,
                serviceNotFound.StatusCode,
                serviceNotFound.Message);

            var operationNotFound = new DispatchException(StatusCode.OperationNotFound, "op not found");
            yield return new TestCaseData(
                operationNotFound,
                operationNotFound.StatusCode,
                operationNotFound.Message);
        }
    }

    [Test, TestCaseSource(nameof(DispatchExceptionSource))]
    public async Task Dispatcher_throws_exception(
        Exception exception,
        StatusCode expectedStatusCode,
        string expectedErrorMessage)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw exception);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        IncomingResponse response = await sut.Client.InvokeAsync(request);
        ReadResult readResult = await response.Payload.ReadAsync();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(response.ErrorMessage, Is.EqualTo(expectedErrorMessage));
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.IsEmpty, Is.True);
    }

    /// <summary>Verifies that disposing a server connection aborts the incoming request underlying stream.
    /// </summary>
    [Test]
    public async Task Dispose_server_connection_aborts_non_completed_incoming_request_stream()
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

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await payload.ReadAsync(), Throws.InstanceOf<IceRpcException>()
            .With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));

        payload.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that an abortive server connection shutdown causes the invocation to fail with <see
    /// cref="IceRpcError.TruncatedData" />.</summary>
    [Test]
    public async Task Abortive_server_connection_shutdown_triggers_payload_read_with_truncated_data()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Client);

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start
        Task shutdownTask = sut.Server.ShutdownAsync();

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(
            async () => await invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));

        Assert.That(async () => await shutdownTask, Throws.Nothing);
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
        Assert.That(() => invokeTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(() => dispatcher.DispatchComplete, Is.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that canceling the invocation while sending the request payload, completes the incoming
    /// request payload with a <see cref="IceRpcError.TruncatedData"/>.</summary>
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
        Assert.That(
            async () => await dispatcher.PayloadReadCompleted,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that canceling the invocation while sending the request payload continuation, completes the
    /// incoming request payload with a <see cref="IceRpcError.TruncatedData"/>.</summary>
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
        Assert.That(
            async () => await dispatcher.PayloadReadCompleted,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
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

        var tcs = new TaskCompletionSource<Exception>();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                clientConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
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
        var exception = new OperationCanceledException(); // can be any exception

        // Act
        payloadContinuation.SetReadException(exception);

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
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));

        Assert.That(async () => await tcs.Task, Is.EqualTo(exception));
    }

    // TODO: see https://github.com/zeroc-ice/icerpc-csharp/issues/2444
    // [TestCase(false, MultiplexedTransportOperation.CreateStream)]
    // [TestCase(true, MultiplexedTransportOperation.CreateStream)]
    [TestCase(false, MultiplexedTransportOperation.AcceptStream)]
    [TestCase(false, MultiplexedTransportOperation.StreamWrite)]
    [TestCase(true, MultiplexedTransportOperation.StreamWrite)]
    public async Task Not_dispatched_request_gets_connection_exception_on_server_connection_shutdown(
        bool isOneway,
        MultiplexedTransportOperation holdOperation)
    {
        // Arrange
        TestMultiplexedClientTransportDecorator? clientTransport = null;
        TestMultiplexedServerTransportDecorator? serverTransport = null;
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton<IMultiplexedServerTransport>(
                provider => serverTransport = new TestMultiplexedServerTransportDecorator(
                        new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>())))
            .AddSingleton<IMultiplexedClientTransport>(
                provider => clientTransport = new TestMultiplexedClientTransportDecorator(
                    new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>())))
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://colochost"))
            .AddIceRpcProtocolTest(
                clientConnectionOptions: new(),
                serverConnectionOptions: new() { Dispatcher = dispatcher })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Client);
        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var invokeTask = sut.Client.InvokeAsync(request1);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Don't accept the next stream.
        switch (holdOperation)
        {
            case MultiplexedTransportOperation.AcceptStream:
            case MultiplexedTransportOperation.StreamRead:
                serverTransport!.LastAcceptedConnection.HoldOperation = holdOperation;
                break;
            case MultiplexedTransportOperation.CreateStream:
            case MultiplexedTransportOperation.StreamWrite:
                clientTransport!.LastConnection.HoldOperation = holdOperation;
                break;
        }

        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)) { IsOneway = isOneway };
        var invokeTask2 = sut.Client.InvokeAsync(request2);

        // Act
        Task shutdownTask = sut.Server.ShutdownAsync();

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(() => invokeTask2);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.OperationRefused));
        dispatcher.ReleaseDispatch();
        Assert.That(() => invokeTask, Throws.Nothing);
        request1.Dispose(); // Necessary to prevent shutdown to wait for the response payload completion.
        Assert.That(() => shutdownTask, Throws.Nothing);
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

        var tcs = new TaskCompletionSource<Exception>();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());
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
        var tcs = new TaskCompletionSource<Exception>();
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());
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
        var tcs = new TaskCompletionSource<Exception>();
        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        Assert.That(
            async () => await responseTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(async () => await tcs.Task, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the payload continuation of a request is completed when the dispatcher does not read this
    /// PipeReader.</summary>
    [Test]
    public async Task PayloadContinuation_of_outgoing_request_completed_when_not_read_by_dispatcher(
        [Values(false, true)] bool isOneway)
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
        var tcs = new TaskCompletionSource<Exception>();
        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                clientConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
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
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());

        // Cleanup
        try
        {
            await responseTask;
        }
        catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.TruncatedData)
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

        var tcs = new TaskCompletionSource<Exception>();

        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadContinuationDecorator.Completed, Is.Null);
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());

        // Cleanup
        try
        {
            await responseTask;
        }
        catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.TruncatedData)
        {
            // Depending on the timing, the payload stream send failure might abort the invocation before the response
            // is sent.
        }
    }

    /// <summary>Ensures that the request payload writer is completed on valid request.</summary>
    [Test]
    public async Task PayloadWriter_completed_with_valid_request()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
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
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);

        // Cleanup
        await responseTask;
    }

    /// <summary>Ensures that the request payload writer is completed on valid response.</summary>
    [Test]
    public async Task PayloadWriter_completed_with_valid_response()
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
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
            .AddProtocolTest(Protocol.IceRpc, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);

        // Cleanup
        await responseTask;
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
        Assert.That(async () => await responseTask, Throws.InstanceOf<InvalidOperationException>());
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
        var tcs = new TaskCompletionSource<Exception>();

        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Not.Null);
        Assert.That(
            async () => await responseTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());
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
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.LimitExceeded));
    }

    [Test]
    public async Task Response_with_header_size_larger_than_max_header_size_fails()
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(
            new OutgoingResponse(request)
            {
                Fields = new Dictionary<ResponseFieldKey, OutgoingFieldValue>
                {
                    [(ResponseFieldKey)3] = new OutgoingFieldValue(new ReadOnlySequence<byte>(new byte[200]))
                }
            }));

        var tcs = new TaskCompletionSource<Exception>();

        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.IceRpc,
                dispatcher,
                clientConnectionOptions: new ConnectionOptions { MaxIceRpcHeaderSize = 100 },
                serverConnectionOptions: new ConnectionOptions
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        Assert.That(
            async () => await sut.Client.InvokeAsync(request),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        Assert.That(
            async () => await tcs.Task,
            Is.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.LimitExceeded));
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

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(
            response.Fields.DecodeValue((ResponseFieldKey)1000, (ref SliceDecoder decoder) => decoder.DecodeString()),
            Is.EqualTo(expectedValue));
    }

    /// <summary>Verifies that a shutdown can be canceled when the server transport ShutdownAsync is hung.</summary>
    [Test]
    [Ignore("See #2404")]
    public async Task Shutdown_cancellation_with_hung_server_transport()
    {
        // Arrange

        // We use our own decorated server transport
        var colocTransport = new ColocTransport();
        var serverTransport = new TestDuplexServerTransportDecorator(
            colocTransport.ServerTransport,
            holdOperation: DuplexTransportOperation.Shutdown);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .AddSingleton(colocTransport.ClientTransport) // overwrite
            .AddSingleton<IDuplexServerTransport>(serverTransport)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Server);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.ShutdownAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static async Task FulfillShutdownRequestAsync(IProtocolConnection connection)
    {
        await connection.ShutdownRequested;
        try
        {
            await connection.ShutdownAsync();
        }
        catch
        {
            // ignore all exceptions
        }
    }

    private static string GetErrorMessage(StatusCode statusCode, Exception innerException)
    {
        var dispatchException = new DispatchException(statusCode);
        return $"{dispatchException.Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}";
    }

    private sealed class HoldPipeReader : PipeReader
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
