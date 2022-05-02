// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests;

public sealed class RetryInterceptorTests
{
    public static IEnumerable<Exception> NotRetryableExceptionSource
    {
        get
        {
            yield return new OperationCanceledException();
            yield return new NoEndpointException();
        }
    }

    [Test]
    public async Task Log_retry()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                throw new ConnectionClosedException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var proxy = new Proxy(Protocol.IceRpc);
        var loggerFactory = new TestLoggerFactory();
        var sut = new RetryInterceptor(invoker, new RetryOptions { LoggerFactory = loggerFactory });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));

        Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
        Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(1));
        TestLoggerEntry entry = loggerFactory.Logger!.Entries[0];
        Assert.That(entry.State["RetryPolicy"], Is.EqualTo(RetryPolicy.Immediately));
        Assert.That(entry.State["Attempt"], Is.EqualTo(2));
        Assert.That(entry.State["MaxAttempts"], Is.EqualTo(2));
        Assert.That(entry.EventId.Id, Is.EqualTo((int)RetryInterceptorEventIds.RetryRequest));
        Assert.That(entry.LogLevel, Is.EqualTo(LogLevel.Information));
        Assert.That(entry.State["Path"], Is.EqualTo("/"));
        Assert.That(entry.State["Operation"], Is.EqualTo("Op"));
        Assert.That(entry.Message, Does.StartWith("retrying request because of retryable exception"));
        Assert.That(entry.Exception, Is.TypeOf<ConnectionClosedException>());
    }

    [Test, TestCaseSource(nameof(NotRetryableExceptionSource))]
    public void Not_retryable_exception(Exception exception)
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attempts++;
            throw exception;
        });

        var proxy = new Proxy(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions());

        var request = new OutgoingRequest(proxy) { Operation = "Op" };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf(exception.GetType()));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task No_retry_with_NoRetry_policy()
    {
        // Arrange
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attempts++;
            request.Features = new FeatureCollection().With(RetryPolicy.NoRetry);
            return Task.FromResult(new IncomingResponse(request, request.Connection!)
            {
                ResultType = ResultType.Failure
            });
        });

        var proxy = new Proxy(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions());

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
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
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                return Task.FromResult(new IncomingResponse(
                    request,
                    request.Connection!,
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
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions());
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy) { Operation = "Op" };

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
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                return Task.FromResult(new IncomingResponse(
                    request,
                    request.Connection!,
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
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var proxy = new Proxy(Protocol.IceRpc);
        var sut = new RetryInterceptor(invoker, new RetryOptions());

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
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
    public void Retry_fails_after_max_attemps()
    {
        // Arrange
        const int maxAttempts = 5;
        int attempts = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attempts++;
            throw new InvalidOperationException();
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttempts });
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy)
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
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                request.IsSent = true;
                throw new ConnectionClosedException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions());
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy) { Operation = "Op" };

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
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                request.IsSent = true;
                throw new InvalidOperationException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions());
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy)
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
        await using var connection1 = new Connection("icerpc://host1");
        await using var connection2 = new Connection("icerpc://host2");
        await using var connection3 = new Connection("icerpc://host3");
        var endpoints = new List<Endpoint>();
        var invoker = new InlineInvoker((request, cancel) =>
        {
            var endpointSelection = request.Features.Get<EndpointSelection>();
            if (endpointSelection?.Endpoint is Endpoint endpoint)
            {
                endpoints.Add(endpoint);
                request.Connection = endpoint.Host switch
                {
                    "host1" => connection1,
                    "host2" => connection2,
                    _ => connection3
                };
            }

            return Task.FromResult(new IncomingResponse(
                request,
                request.Connection!,
                new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                {
                    [ResponseFieldKey.RetryPolicy] = EncodeRetryPolicy(RetryPolicy.OtherReplica)
                })
            {
                ResultType = ResultType.Failure
            });
        });

        var proxy = Proxy.FromConnection(connection1, "/path");
        proxy.Endpoint = connection1.Endpoint;
        proxy.AltEndpoints = new List<Endpoint> { connection2.Endpoint, connection3.Endpoint }.ToImmutableList();
        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
        var start = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(endpoints.Count, Is.EqualTo(3));
        Assert.That(endpoints[0], Is.EqualTo(proxy.Endpoint));
        Assert.That(endpoints[1], Is.EqualTo(proxy.AltEndpoints[0]));
        Assert.That(endpoints[2], Is.EqualTo(proxy.AltEndpoints[1]));
    }

    [Test]
    public async Task RetryPolicy_and_IsSent_property_are_reset_on_retry()
    {
        // Arrange
        int attempts = 0;
        RetryPolicy? retryPolicy = null;
        bool isSent = true;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attempts == 1)
            {
                request.IsSent = true;
                return Task.FromResult(new IncomingResponse(
                    request,
                    request.Connection!,
                    new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                    {
                        [ResponseFieldKey.RetryPolicy] = EncodeRetryPolicy(RetryPolicy.Immediately)
                    })
                {
                    ResultType = ResultType.Failure,
                });
            }
            else
            {
                isSent = request.IsSent;
                retryPolicy = request.Features.Get<RetryPolicy>();
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions());
        var proxy = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(proxy) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(retryPolicy, Is.Null);
        Assert.That(isSent, Is.False);
    }

    private static ReadOnlySequence<byte> EncodeRetryPolicy(RetryPolicy retryPolicy)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        retryPolicy.Encode(ref encoder);
        return new ReadOnlySequence<byte>(buffer.WrittenMemory);
    }
}
