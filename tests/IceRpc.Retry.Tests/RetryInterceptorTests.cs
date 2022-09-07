// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Logger;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
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
                throw new ConnectionClosedException(ConnectionClosedErrorCode.Shutdown);
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

        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

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

        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

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

        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };
        var start = TimeSpan.FromMilliseconds(Environment.TickCount64);

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
        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

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

        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };
        var start = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(TimeSpan.FromMilliseconds(Environment.TickCount64) - start, Is.GreaterThanOrEqualTo(delay));
            Assert.That(attempts, Is.EqualTo(2));
        });
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
        var request = new OutgoingRequest(serviceAddress)
        {
            Operation = "Op"
        };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf<InvalidOperationException>());
        Assert.That(attempts, Is.EqualTo(maxAttempts));
    }

    [Test]
    public async Task Retry_sent_request_after_close_connection()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (++attempts == 1)
            {
                throw new ConnectionClosedException(ConnectionClosedErrorCode.Shutdown);
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.IceRpc));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);
        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_sent_idempotent_request_after_is_sent()
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
        var request = new OutgoingRequest(serviceAddress)
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

        var request = new OutgoingRequest(serviceAddress) { Operation = "Op" };
        var start = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(serverAddresses.Count, Is.EqualTo(3));
        Assert.That(serverAddresses[0], Is.EqualTo(serviceAddress.ServerAddress));
        Assert.That(serverAddresses[1], Is.EqualTo(serviceAddress.AltServerAddresses[0]));
        Assert.That(serverAddresses[2], Is.EqualTo(serviceAddress.AltServerAddresses[1]));
    }

    [Test]
    public async Task Dispatch_exception_with_UnhandledException_is_not_retryable()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                (request, cancellationToken) =>
                {
                    throw new InvalidOperationException();
                }),
                Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/test")));
        var connection = provider.GetRequiredService<ClientConnection>();
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            attempts++;
            return connection.InvokeAsync(request, cancellationToken);
        });
        var sut = new RetryInterceptor(invoker, new RetryOptions(), NullLogger.Instance);

        IncomingResponse response = await sut.InvokeAsync(request, CancellationToken.None);

        // Assert
        RemoteException exception = await response.DecodeFailureAsync(request, new ServiceProxy(connection));
        Assert.Multiple(
            () =>
            {
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(exception, Is.InstanceOf<DispatchException>());
                Assert.That(((DispatchException)exception).ErrorCode, Is.EqualTo(DispatchErrorCode.UnhandledException));
            });
    }

    private static ReadOnlySequence<byte> EncodeRetryPolicy(RetryPolicy retryPolicy)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        retryPolicy.Encode(ref encoder);
        return new ReadOnlySequence<byte>(buffer.WrittenMemory);
    }
}
