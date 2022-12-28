// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
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

    /// <summary>Verifies that disposing a server connection causes the invocation to fail with a <see
    /// cref="DispatchException" />.</summary>
    [Test]
    public async Task Disposing_server_connection_triggers_dispatch_exception([Values(false, true)] bool shutdown)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task? shutdownTask = null;
        if (shutdown)
        {
            shutdownTask = sut.Server.ShutdownAsync();
        }
        await sut.Server.DisposeAsync();

        IncomingResponse response = await invokeTask;

        // Assert
        Assert.That(response.ErrorMessage, Is.EqualTo("The dispatch was canceled by the closure of the connection."));
        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.UnhandledException));

        if (shutdownTask is not null)
        {
            await shutdownTask;
        }
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
                Protocol.Ice,
                dispatcher,
                serverConnectionOptions: new()
                {
                    FaultedTaskAction = tcs.SetResult
                })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act

        // If the response payload writer is bogus the ice connection cannot write any response, the request
        // will be canceled by the timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Assert.That(
            async () => await sut.Client.InvokeAsync(request, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        Assert.That(async () => await tcs.Task, Is.InstanceOf<InvalidOperationException>());
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

        // Act/Assert
        var exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await sut.Client.InvokeAsync(request, default));
        Assert.That(exception, Is.Not.Null);
        exception = Assert.ThrowsAsync<IceRpcException>(async () => await sut.Server.ShutdownComplete);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.IceRpcError));
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

    private static string GetErrorMessage(string Message, Exception innerException) =>
        $"{Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}";

    private static string GetErrorMessage(DispatchException dispatchException) =>
        $"{dispatchException.Message} {{ Original StatusCode = {dispatchException.StatusCode} }}";
}
