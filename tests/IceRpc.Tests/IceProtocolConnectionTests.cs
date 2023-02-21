// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class IceProtocolConnectionTests
{
    private static IEnumerable<TestCaseData> DispatchExceptionSource
    {
        get
        {
            var unhandledException = new DispatchException(StatusCode.UnhandledException);

            var invalidDataException = new InvalidDataException("invalid data");
            yield return new TestCaseData(
                invalidDataException,
                StatusCode.UnhandledException,
                GetErrorMessage(unhandledException.Message, invalidDataException));

            var invalidOperationException = new InvalidOperationException("invalid op message");
            yield return new TestCaseData(
                invalidOperationException,
                StatusCode.UnhandledException,
                GetErrorMessage(unhandledException.Message, invalidOperationException));

            var applicationError = new DispatchException(StatusCode.ApplicationError, "application message");
            yield return new TestCaseData(
                applicationError,
                StatusCode.UnhandledException,
                GetErrorMessage(applicationError));

            var deadlineExpired = new DispatchException(StatusCode.DeadlineExpired, "deadline message");
            yield return new TestCaseData(
                deadlineExpired,
                StatusCode.UnhandledException,
                GetErrorMessage(deadlineExpired));

            var serviceNotFound = new DispatchException(StatusCode.ServiceNotFound);
            yield return new TestCaseData(
                serviceNotFound,
                StatusCode.ServiceNotFound,
                "The dispatch failed with status code ServiceNotFound while dispatching 'op' on '/foo'.");

            var operationNotFound = new DispatchException(StatusCode.OperationNotFound);
            yield return new TestCaseData(
                operationNotFound,
                StatusCode.OperationNotFound,
                "The dispatch failed with status code OperationNotFound while dispatching 'op' on '/foo'.");
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
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice) { Path = "/foo" })
        {
            Operation = "op"
        };

        // Act
        IncomingResponse response = await sut.Client.InvokeAsync(request);
        response.Payload.TryRead(out ReadResult readResult);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));
        Assert.That(response.ErrorMessage, Is.EqualTo(expectedErrorMessage));
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.IsEmpty, Is.True);
    }

    /// <summary>Verifies that an abortive server connection shutdown causes an invocation failure.</summary>
    [Test]
    public async Task Abortive_server_connection_shutdown_triggers_invocation_failure()
    {
        // Arrange
        using var dispatcher = new TestDispatcher(holdDispatchCount: 1);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start
        Task shutdownTask = sut.Server.ShutdownAsync();

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(
            async () => await invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        Assert.That(
            async () => await shutdownTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    [TestCase(false, false, DuplexTransportOperations.Connect)]
    [TestCase(false, false, DuplexTransportOperations.Read)]
    [TestCase(false, true, DuplexTransportOperations.Connect)]
    [TestCase(true, false, DuplexTransportOperations.Connect)]
    [TestCase(true, false, DuplexTransportOperations.Write)]
    [TestCase(true, true, DuplexTransportOperations.Connect)]
    public async Task Connect_exception_handling_on_transport_failure(
        bool serverConnection,
        bool authenticationException,
        DuplexTransportOperations operation)
    {
        // Arrange
        Exception exception = authenticationException ?
            new AuthenticationException() :
            new IceRpcException(IceRpcError.ConnectionRefused);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice)
            .AddTestDuplexTransport(
                clientOperationsOptions: new TransportOperationsOptions<DuplexTransportOperations>
                    {
                        Fail = serverConnection ? DuplexTransportOperations.None : operation,
                        FailureException = exception
                    },
                serverOperationsOptions: new TransportOperationsOptions<DuplexTransportOperations>
                    {
                        Fail = serverConnection ? operation : DuplexTransportOperations.None,
                        FailureException = exception
                    })
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        Func<Task> connectCall = serverConnection ?
            () => sut.ConnectAsync(default) :
            async () =>
            {
                _ = AcceptAsync();
                _ = await sut.Client.ConnectAsync();

                async Task AcceptAsync()
                {
                    try
                    {
                        await sut.AcceptAsync();
                    }
                    catch
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            };

        // Act/Assert
        Exception? caughtException = Assert.CatchAsync(() => connectCall());
        Assert.That(caughtException, Is.EqualTo(exception));
    }

    [TestCase(false, DuplexTransportOperations.Connect)]
    [TestCase(false, DuplexTransportOperations.Read)]
    [TestCase(true, DuplexTransportOperations.Connect)]
    [TestCase(true, DuplexTransportOperations.Write)]
    public async Task Connect_cancellation_on_transport_hang(bool serverConnection, DuplexTransportOperations operation)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice)
            .AddTestDuplexTransport(
                serverOperationsOptions: new() { Hold = serverConnection ? operation : DuplexTransportOperations.None },
                clientOperationsOptions: new() { Hold = serverConnection ? DuplexTransportOperations.None : operation })
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        using var connectCts = new CancellationTokenSource(100);

        Func<Task> connectCall = serverConnection ?
            () => sut.ConnectAsync(connectCts.Token) :
            async () =>
            {
                _ = AcceptAsync();
                _ = await sut.Client.ConnectAsync(connectCts.Token);

                async Task AcceptAsync()
                {
                    try
                    {
                        await sut.AcceptAsync();
                    }
                    catch
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            };

        // Act/Assert
        Assert.That(
            () => connectCall(),
            Throws.InstanceOf<OperationCanceledException>().With.Property(
                "CancellationToken").EqualTo(connectCts.Token));
    }

    [Test]
    public async Task Invoke_exception_handling_on_read_transport_failure()
    {
        // Arrange

        var failureException = new IceRpcException(IceRpcError.IceRpcError);

        // We need a dispatcher to hold the invocation, and ensure the transport read failure happens after we call
        // InvokeAsync and while it is still in course.
        using var dispatcher = new TestDispatcher(holdDispatchCount: 1);
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .AddTestDuplexTransport(clientOperationsOptions: new() { FailureException = failureException })
            .BuildServiceProvider(validateScopes: true);

        var clientTransport = provider.GetRequiredService<TestDuplexClientTransportDecorator>();

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);
        // Wait for the dispatch to start, to ensure the transport read failure happens after we make the invocation.
        await dispatcher.DispatchStart;
        clientTransport!.LastCreatedConnection.Operations.Fail = DuplexTransportOperations.Read;
        dispatcher.ReleaseDispatch();

        // Assert
        Assert.That(() =>
            invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(
                IceRpcError.ConnectionAborted));

        Assert.That(async () => await sut.Client.ShutdownAsync(), Throws.Exception.EqualTo(failureException));
    }

    [Test]
    public async Task Invoke_exception_handling_on_write_transport_failure()
    {
        // Arrange

        // Exceptions thrown by the transport are propagated to the InvokeAsync caller.
        var failureException = new IceRpcException(IceRpcError.IceRpcError);
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice)
            .AddTestDuplexTransport(clientOperationsOptions: new() { FailureException = failureException })
            .BuildServiceProvider(validateScopes: true);

        var clientTransport = provider.GetRequiredService<TestDuplexClientTransportDecorator>();

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        _ = await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        clientTransport!.LastCreatedConnection.Operations.Fail = DuplexTransportOperations.Write;

        // Act
        var invokeTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(() =>
            invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.InvocationCanceled)
                .And.With.Property("InnerException").EqualTo(failureException));

        Assert.That(
            async () => await sut.Client.ShutdownAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted)
                .And.With.Property("InnerException").EqualTo(failureException));
    }

    [Test]
    public async Task Invoke_cancellation_on_duplex_transport_hang([Values] bool oneway)
    {
        // Arrange

        // Make sure the dispatcher does not return the response immediately when oneway is false.
        using var dispatcher = new TestDispatcher(holdDispatchCount: oneway ? 0 : 1);
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .AddTestDuplexTransport(clientOperationsOptions: new() { Hold = DuplexTransportOperations.Write })
            .BuildServiceProvider(validateScopes: true);

        var clientTransport = provider.GetRequiredService<TestDuplexClientTransportDecorator>();

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice)) { IsOneway = oneway };
        using var cts = new CancellationTokenSource();

        Task invokeTask = sut.Client.InvokeAsync(request, cts.Token);
        cts.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        // Act/Assert
        Assert.That(invokeTask.IsCompleted, Is.False);

        clientTransport.LastCreatedConnection.Operations.Hold = DuplexTransportOperations.None;

        if (oneway)
        {
            Assert.That(async () => await invokeTask, Throws.Nothing);
        }
        else
        {
            Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
        }
    }

    /// <summary>Verifies that a timeout mismatch can lead to the idle monitor aborting the connection.</summary>
    [Test]
    public async Task Idle_timeout_mismatch_aborts_connection()
    {
        var clientConnectionOptions = new ConnectionOptions { IceIdleTimeout = TimeSpan.FromSeconds(10) };
        var serverConnectionOptions = new ConnectionOptions { IceIdleTimeout = TimeSpan.FromMilliseconds(100) };

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.Ice,
                dispatcher: null,
                clientConnectionOptions,
                serverConnectionOptions)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        (_, Task serverShutdownRequested) = await sut.ConnectAsync();

        // Act/Assert
        Assert.That(
            async () =>
            {
                await serverShutdownRequested;
                await sut.Server.ShutdownAsync();
            },
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        // Cleanup

        // That's the dispose that actually aborts the connection for the client.
        await sut.Server.DisposeAsync();

        Assert.That(
            async () => await sut.Client.ShutdownAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>Verifies that canceling an invocation while the request is being written does not interrupt the write.
    /// The connection remains active and subsequent request are not affected.</summary>
    [Test]
    public async Task Invocation_cancellation_does_not_interrupt_payload_writing()
    {
        // Arrange
        using var dispatcher = new TestDispatcher(holdDispatchCount: 1);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .AddTestDuplexTransport()
            .BuildServiceProvider(validateScopes: true);

        var clientTransport = provider.GetRequiredService<TestDuplexClientTransportDecorator>();

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.Ice))
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(new byte[1024]))
        };

        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.Ice))
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(new byte[1024]))
        };

        using var cts = new CancellationTokenSource();
        // Hold writes to ensure the invocation blocks writing the request.
        clientTransport.LastCreatedConnection.Operations.Hold = DuplexTransportOperations.Write;

        Task<IncomingResponse> invokeTask1 = sut.Client.InvokeAsync(request1, cts.Token);
        Task<IncomingResponse> invokeTask2 = sut.Client.InvokeAsync(request1, default);
        // Delay to let the connection write start.
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Act
        cts.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.That(invokeTask1.IsCompleted, Is.False);
        clientTransport.LastCreatedConnection.Operations.Hold = DuplexTransportOperations.None;
        Assert.That(async () => await invokeTask1, Throws.TypeOf<OperationCanceledException>());
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask2, Throws.Nothing);
    }

    /// <summary>Verifies that the connection shutdown waits for pending invocations and dispatches to complete.
    /// Requests that are not dispatched by the server complete with an InvocationCanceled error.</summary>
    [Test]
    public async Task Not_dispatched_twoway_request_gets_invocation_canceled_on_server_connection_shutdown()
    {
        // Arrange
        using var dispatcher = new TestDispatcher(holdDispatchCount: 1);
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.Ice,
                dispatcher,
                serverConnectionOptions: new ConnectionOptions
                {
                    MaxDispatches = 1
                })
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        (Task clientShutdownRequested, _) = await sut.ConnectAsync();
        _ = sut.Client.ShutdownWhenRequestedAsync(clientShutdownRequested);

        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var invokeTask1 = sut.Client.InvokeAsync(request1);

        // Act
        await dispatcher.DispatchStart;
        var shutdownTask = sut.Server.ShutdownAsync();

        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var invokeTask2 = sut.Client.InvokeAsync(request2);
        dispatcher.ReleaseDispatch();

        // Assert
        Assert.That(async () => await invokeTask1, Throws.Nothing);
        Assert.That(async () => await shutdownTask, Throws.Nothing);

        Assert.That(
            async () => await invokeTask2,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.InvocationCanceled));
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
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        Assert.That(
            async () => (await sut.Client.InvokeAsync(request)).StatusCode,
            Is.EqualTo(StatusCode.UnhandledException));
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
    }

    [Test]
    public async Task Receiving_a_frame_larger_than_max_ice_frame_size_aborts_the_connection()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.Ice,
                serverConnectionOptions: new ConnectionOptions
                {
                    MaxIceFrameSize = 256
                })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        (_, Task serverShutdownRequested) = await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[1024]));
        pipe.Writer.Complete();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice))
        {
            Payload = pipe.Reader
        };

        // Act
        Task invokeTask = sut.Client.InvokeAsync(request, default);

        // Assert
        Assert.That(
            async () =>
            {
                await serverShutdownRequested;
                await sut.Server.ShutdownAsync();
            },
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        // Cleanup
        await sut.Server.DisposeAsync();

        Assert.That(
            async () => await invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>This test verifies that responses that are received after a request1 has been discarded are ignored,
    /// and doesn't interfere with other request1 and responses being send over the same connection.</summary>
    [Test]
    public async Task Response_received_for_discarded_request_is_ignored()
    {
        // Arrange
        using var dispatcher = new TestDispatcher(new byte[200], holdDispatchCount: 1);
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request1 = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        using var request2 = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        using var cts = new CancellationTokenSource();

        // Act/Assert
        var invokeTask = sut.Client.InvokeAsync(request1, cts.Token);
        await dispatcher.DispatchStart;
        cts.Cancel();
        Assert.That(async () => await invokeTask, Throws.InstanceOf<OperationCanceledException>());
        // Let the dispatch continue after the invocation was discarded
        dispatcher.ReleaseDispatch();

        Assert.That(dispatcher.ResponsePayload, Is.Not.Null);
        Assert.That(async () => await dispatcher.ResponsePayload!.Completed, Throws.Nothing);

        var response = await sut.Client.InvokeAsync(request2, default);
        // with ice, the payload is fully available at this point
        bool ok = response.Payload.TryRead(out ReadResult readResult);
        Assert.That(ok, Is.True);
        Assert.That(readResult.IsCompleted, Is.True);
    }

    private static string GetErrorMessage(string Message, Exception innerException) =>
        $"{Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}";

    private static string GetErrorMessage(DispatchException dispatchException) =>
        $"{dispatchException.Message} {{ Original StatusCode = {dispatchException.StatusCode} }}";
}
