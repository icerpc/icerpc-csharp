// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Logger;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Retry.Tests;

public sealed class RetryInterceptorTests
{
    public static IEnumerable<Exception> NotRetryableExceptionSource
    {
        get
        {
            yield return new OperationCanceledException();
            yield return new NoServerAddressException();
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
                throw new ConnectionException(ConnectionErrorCode.ClosedByPeer);
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
        Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));
        TestLoggerEntry entry = loggerFactory.Logger!.Entries[1];
        Assert.That(entry.Scope["RetryPolicy"], Is.EqualTo(RetryPolicy.Immediately));
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

    [Test]
    public async Task No_retry_with_NoRetry_policy()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            attempts++;
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc)
            {
                ResultType = ResultType.Failure
            });
        });

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task Response_payload_is_completed_on_retry()
    {
        // Arrange
        int attempts = 0;
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                return Task.FromResult(new IncomingResponse(
                    request,
                    FakeConnectionContext.IceRpc,
                    new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                    {
                        [ResponseFieldKey.RetryPolicy] = EncodeRetryPolicy(RetryPolicy.Immediately)
                    })
                {
                    ResultType = ResultType.Failure,
                    Payload = payloadDecorator
                });
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(await payloadDecorator.Completed, Is.Null);
    }

    [Test]
    public async Task Retry_after_delay_with_AfterDelay_policy()
    {
        // Arrange
        int attempts = 0;
        var delay = TimeSpan.FromMilliseconds(200);
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                return Task.FromResult(new IncomingResponse(
                    request,
                    FakeConnectionContext.IceRpc,
                    new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                    {
                        [ResponseFieldKey.RetryPolicy] = EncodeRetryPolicy(RetryPolicy.AfterDelay(delay))
                    })
                {
                    ResultType = ResultType.Failure
                });
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };
        var stopwatch = Stopwatch.StartNew();

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        stopwatch.Stop();
        Assert.That(
            // TimeSpan rounded to the nearest millisecond
            TimeSpan.FromMilliseconds(Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0)),
            Is.GreaterThanOrEqualTo(delay));
        Assert.That(attempts, Is.EqualTo(2));
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
            throw new InvalidOperationException();
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttempts }, NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress)
        {
            Operation = "Op"
        };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf<InvalidOperationException>());
        Assert.That(attempts, Is.EqualTo(maxAttempts));
    }

    [Test]
    public async Task Retry_if_payload_was_not_read()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                throw new InvalidOperationException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_on_connection_closed()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                // The retry interceptor assumes that it is always safe to retry when the payload was not read, the reading
                // of the payload here ensures that retry is due to the connection closed exception we are testing for.
                ReadResult readResult;
                do
                {
                    readResult = await request.Payload.ReadAsync(cancellationToken);
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                }
                while (!readResult.IsCompleted && !readResult.IsCanceled);

                throw new ConnectionException(ConnectionErrorCode.ClosedByPeer);
            }
            else
            {
                return new IncomingResponse(request, FakeConnectionContext.IceRpc);
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_idempotent_request()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                // The retry interceptor assumes that it is always safe to retry when the payload was not read, the reading
                // of the payload here ensures that retry is due to request invocation mode being idempotent.
                ReadResult readResult;
                do
                {
                    readResult = await request.Payload.ReadAsync(cancellationToken);
                    request.Payload.AdvanceTo(readResult.Buffer.End);
                }
                while (!readResult.IsCompleted && !readResult.IsCanceled);

                throw new InvalidOperationException();
            }
            else
            {
                return new IncomingResponse(request, FakeConnectionContext.IceRpc);
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

    [Test]
    public async Task Retry_with_OtherReplica_policy()
    {
        // Arrange
        await using var connection1 = new ClientConnection(new Uri("icerpc://host1"));
        await using var connection2 = new ClientConnection(new Uri("icerpc://host2"));
        await using var connection3 = new ClientConnection(new Uri("icerpc://host3"));
        var serverAddresses = new List<ServerAddress>();
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (request.Features.Get<IServerAddressFeature>() is not IServerAddressFeature serverAddressFeature)
            {
                serverAddressFeature = new ServerAddressFeature(request.ServiceAddress);
                request.Features = request.Features.With(serverAddressFeature);
            }
            if (serverAddressFeature.ServerAddress is ServerAddress serverAddress)
            {
                serverAddresses.Add(serverAddress);
            }

            return Task.FromResult(new IncomingResponse(
                request,
                FakeConnectionContext.IceRpc,
                new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                {
                    [ResponseFieldKey.RetryPolicy] = EncodeRetryPolicy(RetryPolicy.OtherReplica)
                })
            {
                ResultType = ResultType.Failure
            });
        });

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

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 }, NullLogger.Instance);

        using var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(serverAddresses.Count, Is.EqualTo(3));
        Assert.That(serverAddresses[0], Is.EqualTo(serviceAddress.ServerAddress));
        Assert.That(serverAddresses[1], Is.EqualTo(serviceAddress.AltServerAddresses[0]));
        Assert.That(serverAddresses[2], Is.EqualTo(serviceAddress.AltServerAddresses[1]));
    }

    private static ReadOnlySequence<byte> EncodeRetryPolicy(RetryPolicy retryPolicy)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        retryPolicy.Encode(ref encoder);
        return new ReadOnlySequence<byte>(buffer.WrittenMemory);
    }
}
