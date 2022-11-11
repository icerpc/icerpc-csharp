// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class IceProtocolConnectionTests
{
    public static IEnumerable<TestCaseData> ResponseRetryPolicySource
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

    /// <summary>Verifies that disposing a server connection causes the invocation to fail with <see
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
        Assert.That(
            async () => (await response.DecodeDispatchExceptionAsync(request)).Message,
            Is.EqualTo("dispatch canceled"));
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

    /// <summary>Ensures that the response payload continuation is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadContinuation_completed_on_response()
    {
        // Arrange
        var payloadContinuationDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    PayloadContinuation = payloadContinuationDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(async () => await payloadContinuationDecorator.Completed, Throws.Nothing);

        // Cleanup
        await responseTask;
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
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        DispatchException exception = Assert.ThrowsAsync<DispatchException>(
            async () =>
            {
                var response = await responseTask;
                var exception = await response.DecodeDispatchExceptionAsync(request, CancellationToken.None);
                throw exception;
            });
        Assert.That(exception.StatusCode, Is.EqualTo(StatusCode.UnhandledException));
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
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request, cts.Token);

        // Assert
        Assert.That(async () => await payloadDecorator.Completed, Throws.Nothing);
        // If the response payload writer the ice connection cannot write any response, the request will be
        // canceled by the timeout.
        Assert.That(async () => await responseTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Shutting down a non-connected server connection disposes the underlying transport connection.
    /// </summary>
    [Test]
    public async Task Shutdown_non_connected_connection_disposes_underlying_transport_connection()
    {
        // Arrange
        IListener<IDuplexConnection> transportListener = IDuplexServerTransport.Default.Listen(
            new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
            new DuplexConnectionOptions(),
            null);

        await using IListener<IProtocolConnection> listener =
            new IceProtocolListener(new ConnectionOptions(), transportListener);

        IDuplexConnection clientTransport = IDuplexClientTransport.Default.CreateConnection(
            transportListener.ServerAddress,
            new DuplexConnectionOptions(), null);

        await using var clientConnection =
            new IceProtocolConnection(clientTransport, false, new ClientConnectionOptions());

        _ = Task.Run(async () =>
        {
            (IProtocolConnection connection, _) = await listener.AcceptAsync(default);
            _ = connection.ShutdownAsync();
        });

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            () => clientConnection.ConnectAsync(default));
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.TransportError));
        var transportException = exception.InnerException as TransportException;
        Assert.That(transportException!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
    }
}
