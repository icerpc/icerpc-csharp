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
    public static IEnumerable<TestCaseData> ExceptionIsEncodedAsADispatchExceptionSource
    {
        get
        {
            // an unexpected OCE
            yield return new TestCaseData(new OperationCanceledException(), DispatchErrorCode.UnhandledException);

            yield return new TestCaseData(new InvalidDataException("invalid data"), DispatchErrorCode.InvalidData);
            yield return new TestCaseData(new MyException(), DispatchErrorCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), DispatchErrorCode.UnhandledException);
        }
    }

    public static IEnumerable<TestCaseData> DispatchExceptionRetryPolicySource
    {
        get
        {
            // Service not found failure with a service address that has no server address gets OtherReplica retry
            // policy response field.
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
                DispatchErrorCode.ServiceNotFound,
                RetryPolicy.OtherReplica);

            // Service not found failure with a service address that has server addresses does not get a retry policy
            // response field
            yield return new TestCaseData(
                new ServiceAddress(new Uri("ice://localhost/service")),
                DispatchErrorCode.ServiceNotFound,
                null);

            // No retry policy field with other dispatch errors
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
                DispatchErrorCode.UnhandledException,
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

        // Assert
        var ex = Assert.ThrowsAsync<DispatchException>(
            async () =>
            {
                IncomingResponse response = await invokeTask;
                throw await response.DecodeFailureAsync(request, new ServiceProxy(sut.Client));
            });
        Assert.That(ex!.Message, Is.EqualTo("dispatch canceled"));
    }

    /// <summary>Verifies that a failure response contains the expected retry policy field.</summary>
    [Test, TestCaseSource(nameof(DispatchExceptionRetryPolicySource))]
    public async Task Dispatch_failure_response_contain_the_expected_retry_policy_field(
        ServiceAddress serviceAddress,
        DispatchErrorCode errorCode,
        RetryPolicy? expectedRetryPolicy)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(
            (request, cancellationToken) => throw new DispatchException(errorCode: errorCode));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(serviceAddress);

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var retryPolicy = response.Fields.DecodeValue(
                ResponseFieldKey.RetryPolicy,
                (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
        Assert.That(retryPolicy, Is.EqualTo(expectedRetryPolicy));
    }

    /// <summary>Verifies that with the ice protocol, when a exception other than a DispatchException is thrown
    /// during the dispatch, we encode a DispatchException with the expected error code.</summary>
    [Test, TestCaseSource(nameof(ExceptionIsEncodedAsADispatchExceptionSource))]
    public async Task Exception_is_encoded_as_a_dispatch_exception(
        Exception thrownException,
        DispatchErrorCode errorCode)
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw thrownException);

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync(request, new ServiceProxy(sut.Client)) as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ErrorCode, Is.EqualTo(errorCode));
    }

    /// <summary>Ensures that the response payload stream is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadStream_completed_on_response()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
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
