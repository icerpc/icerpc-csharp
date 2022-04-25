// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests;

public sealed class RetryInterceptorTests
{
    public static IEnumerable<Exception> NoRetryableExceptionSource
    {
        get
        {
            yield return new OperationCanceledException();
            yield return new NoEndpointException();
        }
    }

    [Test, TestCaseSource(nameof(NoRetryableExceptionSource))]
    public async Task No_retryable_exception(Exception exception)
    {
        // Arrange
        int attemps = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attemps++;
            throw exception;
        });

        await using var connection = new Connection("icerpc://localhost");
        var proxy = Proxy.FromConnection(connection, "/path");
        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf(exception.GetType()));
        Assert.That(attemps, Is.EqualTo(1));
    }

    [Test]
    public async Task No_retryable_retry_policy()
    {
        // Arrange
        int attemps = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attemps++;
            request.Features = new FeatureCollection().With(RetryPolicy.NoRetry);
            return Task.FromResult(new IncomingResponse(request, request.Connection!)
            {
                ResultType = ResultType.Failure
            });
        });

        await using var connection = new Connection("icerpc://localhost");
        var proxy = Proxy.FromConnection(connection, "/path");
        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
        var start = Time.Elapsed;

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(attemps, Is.EqualTo(1));
    }

    [Test]
    public async Task Retry_after_delay_retry_policy()
    {
        // Arrange
        int attemps = 0;
        var delay = TimeSpan.FromMilliseconds(200);
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attemps == 1)
            {
                request.Features = new FeatureCollection().With(RetryPolicy.AfterDelay(delay));
                return Task.FromResult(new IncomingResponse(request, request.Connection!)
                {
                    ResultType = ResultType.Failure
                });
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        await using var connection = new Connection("icerpc://localhost");
        var proxy = Proxy.FromConnection(connection, "/path");
        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
        var start = Time.Elapsed;

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Time.Elapsed - start, Is.GreaterThanOrEqualTo(delay - TimeSpan.FromMilliseconds(1)));
        Assert.That(attemps, Is.EqualTo(2));
    }

    [Test]
    public void Retry_fails_after_max_attemps()
    {
        // Arrange
        int maxAttemps = 3;
        int attemps = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            attemps++;
            throw new InvalidOperationException();
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttemps });

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" })
        {
            Operation = "Op"
        };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.TypeOf<InvalidOperationException>());
        Assert.That(attemps, Is.EqualTo(maxAttemps));
    }

    [Test]
    public async Task Retry_request_after_close_connection()
    {
        // Arrange
        int maxAttemps = 3;
        int attemps = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attemps == 1)
            {
                request.IsSent = true;
                throw new ConnectionClosedException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttemps });
        var proxy = new Proxy(Protocol.IceRpc) { Path = "/path" };
        var request = new OutgoingRequest(proxy) { Operation = "Op" };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(attemps, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_idempotent_request_after_is_sent()
    {
        // Arrange
        int maxAttemps = 3;
        int attemps = 0;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (++attemps == 1)
            {
                request.IsSent = true;
                throw new InvalidOperationException();
            }
            else
            {
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }
        });

        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = maxAttemps });
        var proxy = new Proxy(Protocol.IceRpc) { Path = "/path" };
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
        Assert.That(attemps, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_other_replica()
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

            request.Features = request.Features.With(RetryPolicy.OtherReplica);
            return Task.FromResult(new IncomingResponse(request, request.Connection!)
            {
                ResultType = ResultType.Failure
            });
        });

        var proxy = Proxy.FromConnection(connection1, "/path");
        proxy.Endpoint = connection1.Endpoint;
        proxy.AltEndpoints = new List<Endpoint>{ connection2.Endpoint, connection3.Endpoint }.ToImmutableList();
        var sut = new RetryInterceptor(invoker, new RetryOptions { MaxAttempts = 3 });

        var request = new OutgoingRequest(proxy) { Operation = "Op" };
        var start = Time.Elapsed;

        // Act
        var response = await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        Assert.That(endpoints.Count, Is.EqualTo(3));
        Assert.That(endpoints[0], Is.EqualTo(proxy.Endpoint));
        Assert.That(endpoints[1], Is.EqualTo(proxy.AltEndpoints[0]));
        Assert.That(endpoints[2], Is.EqualTo(proxy.AltEndpoints[1]));
    }
}
