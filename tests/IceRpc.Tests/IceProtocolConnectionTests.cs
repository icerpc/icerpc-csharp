// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

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

    [TestCase(false, false, DuplexTransportOperation.Connect)]
    [TestCase(false, false, DuplexTransportOperation.Read)]
    [TestCase(false, true, DuplexTransportOperation.Connect)]
    [TestCase(true, false, DuplexTransportOperation.Connect)]
    [TestCase(true, false, DuplexTransportOperation.Write)]
    [TestCase(true, true, DuplexTransportOperation.Connect)]
    public async Task Connect_exception_handling_on_transport_failure(
        bool serverConnection,
        bool authenticationException,
        DuplexTransportOperation operation)
    {
        // Arrange
        Exception exception = authenticationException ?
            new System.Security.Authentication.AuthenticationException() :
            new IceRpcException(IceRpcError.ConnectionRefused);

        var colocTransport = new ColocTransport();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton<IDuplexServerTransport>(
                provider => new TestDuplexServerTransportDecorator(
                    colocTransport.ServerTransport,
                    failOperation: serverConnection ? operation : DuplexTransportOperation.None,
                    failureException: exception))
            .AddSingleton<IDuplexClientTransport>(
                provider => new TestDuplexClientTransportDecorator(
                    colocTransport.ClientTransport,
                    failOperation: serverConnection ? DuplexTransportOperation.None : operation,
                    failureException: exception))
            .AddDuplexTransportClientServerTest(new Uri("ice://colochost"))
            .AddIceProtocolTest()
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

        // The protocol connection is not created if server-side connect fails.
        if (!serverConnection || operation != DuplexTransportOperation.Connect)
        {
            Assert.That(() => serverConnection ? sut.Server.Closed : sut.Client.Closed, Is.EqualTo(exception));
        }
    }

    [TestCase(false, DuplexTransportOperation.Connect)]
    [TestCase(false, DuplexTransportOperation.Read)]
    [TestCase(true, DuplexTransportOperation.Connect)]
    [TestCase(true, DuplexTransportOperation.Write)]
    public async Task Connect_cancellation_on_transport_hang(
        bool serverConnection,
        DuplexTransportOperation operation)
    {
        // Arrange
        var colocTransport = new ColocTransport();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton<IDuplexServerTransport>(
                provider => new TestDuplexServerTransportDecorator(
                    colocTransport.ServerTransport,
                    holdOperation: serverConnection ? operation : DuplexTransportOperation.None))
            .AddSingleton<IDuplexClientTransport>(
                provider => new TestDuplexClientTransportDecorator(
                    colocTransport.ClientTransport,
                    holdOperation: serverConnection ? DuplexTransportOperation.None : operation))
            .AddDuplexTransportClientServerTest(new Uri("ice://colochost"))
            .AddIceProtocolTest()
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
        OperationCanceledException? exception = Assert.CatchAsync<OperationCanceledException>(() => connectCall());
        Assert.That(exception!.CancellationToken, Is.EqualTo(connectCts.Token));
    }

    /// <summary>Verifies that a timeout mismatch can lead to the idle monitor aborting the connection.</summary>
    [Test]
    public async Task Idle_timeout_mismatch_aborts_connection()
    {
        var clientConnectionOptions = new ConnectionOptions { IdleTimeout = TimeSpan.FromSeconds(10) };
        var serverConnectionOptions = new ConnectionOptions { IdleTimeout = TimeSpan.FromMilliseconds(100) };

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.Ice,
                dispatcher: null,
                clientConnectionOptions,
                serverConnectionOptions)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        await sut.Server.ShutdownRequested; // after about 100 ms, the server connection requests a shutdown

        // Assert
        Assert.That(
            async () => await sut.Server.Closed,
            Is.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        // Cleanup

        // That's the dispose that actually aborts the connection for the client. It's usually triggered by Closed's
        // completion.
        await sut.Server.DisposeAsync();

        Assert.That(
            async () => await sut.Client.ShutdownAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>Verifies that canceling an invocation that is writing the payload, fails with
    /// OperationCanceledException and lets the payload write finish in the background.</summary>
    [Test]
    public async Task Invocation_cancelation_lets_payload_writting_continue_on_background()
    {
        using var dispatcher = new TestDispatcher();
        var colocTransport = new ColocTransport();

        var clientTransport = new TestDuplexClientTransportDecorator(colocTransport.ClientTransport);

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .AddSingleton(colocTransport.ServerTransport)
            .AddSingleton<IDuplexClientTransport>(clientTransport)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[1024]);
        pipe.Writer.Complete();
        var payload = new ReadsCompletedPipeReaderDecorator(pipe.Reader);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice))
        {
            Payload = payload
        };

        using var cts = new CancellationTokenSource();
        // Hold writes to ensure the invocation blocks writing the payload.
        clientTransport.LastConnection.HoldOperation = DuplexTransportOperation.Write;

        // Act/Assert

        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request, cts.Token);
        // Wait for the connection to read the payload, and let write start.
        await payload.ReadsCompleted;
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Ensure Invoke didn't complete and dispatch didn't start
        Assert.That(invokeTask.IsCompleted, Is.False);
        Assert.That(dispatcher.DispatchStart.IsCompleted, Is.False);

        // Cancel the invocation, and let invoke fail.
        cts.Cancel();
        Assert.That(async () => await invokeTask, Throws.TypeOf<OperationCanceledException>());
        Assert.That(dispatcher.DispatchStart.IsCompleted, Is.False);

        // Unblock writes and let them continue on the background
        clientTransport.LastConnection.HoldOperation = DuplexTransportOperation.None;
        IncomingRequest incomingRequest = await dispatcher.DispatchStart;
        incomingRequest.Payload.TryRead(out ReadResult readResult);
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.Length, Is.EqualTo(1024));
        Assert.That(async () => await dispatcher.DispatchComplete, Is.Null);
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
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Client);

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
        await sut.ConnectAsync();
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
            await sut.Server.Closed,
            Is.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        // Cleanup
        await sut.Server.DisposeAsync();

        Assert.That(
            async () => await invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>This test verifies that responses that are received after a request has been discarded are ignored,
    /// and doesn't interfere with other request and responses being send over the same connection.</summary>
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

    private static string GetErrorMessage(string Message, Exception innerException) =>
        $"{Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}";

    private static string GetErrorMessage(DispatchException dispatchException) =>
        $"{dispatchException.Message} {{ Original StatusCode = {dispatchException.StatusCode} }}";

    private class ReadsCompletedPipeReaderDecorator : PipeReader
    {
        public Task ReadsCompleted => _readsCompleted.Task;

        private PipeReader _decoratee;
        private TaskCompletionSource _readsCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _decoratee.CancelPendingRead();

        public override void Complete(Exception? exception) => _decoratee.CancelPendingRead();

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            ReadResult readResult = await _decoratee.ReadAsync(cancellationToken);
            if (readResult.IsCompleted)
            {
                _readsCompleted.SetResult();
            }
            return readResult;
        }

        public override bool TryRead(out ReadResult readResult)
        {
            bool result = _decoratee.TryRead(out readResult);
            if (readResult.IsCompleted)
            {
                _readsCompleted.SetResult();
            }
            return result;
        }

        internal ReadsCompletedPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;
    }
}
