// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
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
                invalidDataException ,
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

    private static IEnumerable<TestCaseData> ResponseRetryPolicySource
    {
        get
        {
            // Service not found failure with a service address that has no server address gets OtherReplica retry
            // policy response field.
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
                StatusCode.ServiceNotFound,
                RetryPolicy.OtherReplica);

            // Service not found failure with a service address that has server addresses does not get a retry policy
            // response field
            yield return new TestCaseData(
                new ServiceAddress(new Uri("ice://localhost/service")),
                StatusCode.ServiceNotFound,
                null);

            // No retry policy field with other dispatch errors
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
                StatusCode.UnhandledException,
                null);
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
        if (shutdown)
        {
            _ = sut.Server.ShutdownAsync();
        }
        await sut.Server.DisposeAsync();

        IncomingResponse response = await invokeTask;

        // Assert
        Assert.That(response.ErrorMessage, Is.EqualTo("dispatch canceled"));
        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.UnhandledException));
    }

    /// <summary>Verifies that a non-success response contains the expected retry policy field.</summary>
    [Test, TestCaseSource(nameof(ResponseRetryPolicySource))]
    public async Task Response_contains_the_expected_retry_policy_field(
        ServiceAddress serviceAddress,
        StatusCode statusCode,
        RetryPolicy? expectedRetryPolicy)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(
            (request, cancellationToken) => throw new DispatchException(statusCode: statusCode));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(serviceAddress);

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(statusCode));
        var retryPolicy = response.Fields.DecodeValue(
                ResponseFieldKey.RetryPolicy,
                (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
        Assert.That(retryPolicy, Is.EqualTo(expectedRetryPolicy));
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
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
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
    }

    private static string GetErrorMessage(string Message, Exception innerException) =>
        $"{Message} This exception was caused by an exception of type '{innerException.GetType()}' with message: {innerException.Message}";

    private static string GetErrorMessage(DispatchException dispatchException) =>
        $"{dispatchException.Message} {{ Original StatusCode = {dispatchException.StatusCode} }}";
}
