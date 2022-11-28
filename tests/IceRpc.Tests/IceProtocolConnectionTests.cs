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
        var exception = Assert.ThrowsAsync<ConnectionException>(
            async () => await sut.Client.InvokeAsync(request, default));
        Assert.That(exception.ErrorCode, Is.EqualTo(ConnectionErrorCode.TransportError));
        exception = Assert.ThrowsAsync<ConnectionException>(async () => await sut.Server.ShutdownComplete);
        Assert.That(exception.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByAbort));
    }
}
