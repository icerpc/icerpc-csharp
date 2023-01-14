// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Logger;
using IceRpc.Tests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Retry.Tests;

public sealed class RetryInterceptorTests
{
    public static IEnumerable<Exception> NotRetryableExceptionSource
    {
        get
        {
            yield return new OperationCanceledException();
            yield return new IceRpcException(IceRpcError.NoConnection);
            yield return new ArgumentException();
            yield return new IceRpcException(IceRpcError.IceRpcError);
        }
    }

    // Status code we never retry on
    public static IEnumerable<StatusCode> NotRetryableStatusCodeSource { get; } = new StatusCode[]
    {
        StatusCode.ApplicationError,
        StatusCode.OperationNotFound,
        StatusCode.UnhandledException,
        StatusCode.InvalidData,
        StatusCode.TruncatedPayload,
        StatusCode.DeadlineExpired,
        StatusCode.Unauthorized,
    };

    public static IEnumerable<TestCaseData> RetryWithOtherReplicaSource
    {
        get
        {
            yield return new TestCaseData(Protocol.IceRpc, FailWithException(IceRpcError.ConnectionRefused))
                .SetName("Retry_with_other_replica(icerpc, IceRpcError.ConnectionRefused)");

            yield return new TestCaseData(Protocol.IceRpc, FailWithException(IceRpcError.ServerBusy))
                .SetName("Retry_with_other_replica(icerpc, IceRpcError.ServerBusy)");

            yield return new TestCaseData(
                Protocol.IceRpc,
                new InlineInvoker((request, cancel) =>
                    Task.FromResult(new IncomingResponse(
                        request,
                        FakeConnectionContext.IceRpc,
                        StatusCode.Unavailable,
                        "error message")))).SetName("Retry_with_other_replica(icerpc, StatusCode.Unavailable)");

            yield return new TestCaseData(
                Protocol.Ice,
                new InlineInvoker((request, cancel) =>
                    Task.FromResult(new IncomingResponse(
                        request,
                        FakeConnectionContext.Ice,
                        StatusCode.ServiceNotFound,
                        "error message")))).SetName("Retry_with_other_replica(ice, StatusCode.ServiceNotFound)");

#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations.
            // The warning is bogus. The property doesn't raise any exceptions the returned invoker does.
            static IInvoker FailWithException(IceRpcError error) =>
                new InlineInvoker((request, cancel) => throw new IceRpcException(error));
#pragma warning restore CA1065
        }
    }

    [Test]
    public async Task Log_retry()
    {
        // Arrange
        int attempts = 0;
        IInvoker invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                throw new IceRpcException(IceRpcError.InvocationCanceled);
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var loggerFactory = new TestLoggerFactory();

        invoker = new LoggerInterceptor(invoker, loggerFactory.CreateLogger<LoggerInterceptor>());

        var sut = new RetryInterceptor(invoker, new RetryOptions(), loggerFactory.CreateLogger<RetryInterceptor>());

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));

        Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc.Logger.LoggerInterceptor"));

        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();
        // The first entry doesn't correspond to a retry and has an empty scope
        Assert.That(entry.Scope, Is.Empty);

        entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();
        Assert.That(entry.Scope["Attempt"], Is.EqualTo(2));
        Assert.That(entry.Scope["MaxAttempts"], Is.EqualTo(2));
        Assert.That(entry.LogLevel, Is.EqualTo(LogLevel.Information));
        Assert.That(entry.State["ServiceAddress"], Is.EqualTo(serviceAddress));
        Assert.That(entry.State["Operation"], Is.EqualTo("Op"));
    }

    [Test, TestCaseSource(nameof(NotRetryableExceptionSource))]
    public void Not_retryable_exception(Exception exception)
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            attempts++;
            throw exception;
        });

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf(exception.GetType()));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test, TestCaseSource(nameof(NotRetryableStatusCodeSource))]
    public async Task No_retry_status_code(StatusCode statusCode)
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            attempts++;
            return Task.FromResult(
                new IncomingResponse(
                    request,
                    FakeConnectionContext.IceRpc,
                    statusCode,
                    ""));
        });

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        IncomingResponse response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(statusCode));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void Retry_fails_after_max_attempts()
    {
        // Arrange
        const int maxAttempts = 5;
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            attempts++;
            throw new IceRpcException(IceRpcError.InvocationCanceled);
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttempts }, NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress)
        {
            Operation = "Op"
        };

        // Act/Assert
        Assert.That(
            async () => await sut.InvokeAsync(request, default),
            Throws.TypeOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.InvocationCanceled));
        Assert.That(attempts, Is.EqualTo(maxAttempts));
    }

    [Test]
    public async Task Retry_idempotent_request_when_failed_with_connection_aborted()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                throw new IceRpcException(IceRpcError.ConnectionAborted);
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress)
        {
            Fields = new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [RequestFieldKey.Idempotent] = new OutgoingFieldValue(new ReadOnlySequence<byte>())
            },
            Operation = "Op"
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test, TestCaseSource(nameof(RetryWithOtherReplicaSource))]
    public async Task Retry_with_other_replica(Protocol protocol, IInvoker next)
    {
        // Arrange
        await using var connection1 = new ClientConnection(new Uri($"{protocol.Name}://host1"));
        await using var connection2 = new ClientConnection(new Uri($"{protocol.Name}://host2"));
        await using var connection3 = new ClientConnection(new Uri($"{protocol.Name}://host3"));

        var serviceAddress = new ServiceAddress(connection1.ServerAddress.Protocol)
        {
            Path = "/path",
            ServerAddress = connection1.ServerAddress,
            AltServerAddresses = new List<ServerAddress>
            {
                connection2.ServerAddress,
                connection3.ServerAddress
            }.ToImmutableList()
        };

        var serverAddresses = new List<ServerAddress>();
        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            if (request.Features.Get<IServerAddressFeature>() is not IServerAddressFeature serverAddressFeature)
            {
                serverAddressFeature = new ServerAddressFeature(request.ServiceAddress);
                request.Features = request.Features.With(serverAddressFeature);
            }
            if (serverAddressFeature.ServerAddress is ServerAddress serverAddress)
            {
                serverAddresses.Add(serverAddress);
                if (serverAddress == serviceAddress.AltServerAddresses[1])
                {
                    return new IncomingResponse(
                        request,
                        request.Protocol == Protocol.IceRpc ? FakeConnectionContext.IceRpc : FakeConnectionContext.Ice,
                        StatusCode.Success);
                }
            }

            return await next.InvokeAsync(request, cancellationToken);
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 }, NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(StatusCode.Success));
        Assert.That(serverAddresses, Has.Count.EqualTo(3));
        Assert.That(serverAddresses[0], Is.EqualTo(serviceAddress.ServerAddress));
        Assert.That(serverAddresses[1], Is.EqualTo(serviceAddress.AltServerAddresses[0]));
        Assert.That(serverAddresses[2], Is.EqualTo(serviceAddress.AltServerAddresses[1]));
    }
}
