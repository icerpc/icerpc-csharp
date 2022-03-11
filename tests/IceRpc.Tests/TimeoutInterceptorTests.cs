// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Internal;
using IceRpc.Transports;

namespace IceRpc.Tests;

public sealed class TimeoutInterceptorTests
{
    /// <summary>Verifies that <see cref="PipelineExtensions.UseTimeout(Pipeline, TimeSpan)"/> doesn't allow to use an
    /// invalid invocation timeout value.</summary>
    /// <param name="timeout">The invalid timeout value for the test.</param>
    [TestCase(-2)] // Cannot use a negative timeout
    [TestCase(-1)] // Cannot use infinite timeout
    public void Cannot_use_an_invalid_timeout_with_the_timeout_interceptor(int timeout)
    {
        // Arrange
        var pipeline = new Pipeline();
        pipeline.UseTimeout(TimeSpan.FromSeconds(timeout));
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = pipeline,
        };

        // Act & Assert 
        Assert.ThrowsAsync<ArgumentException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                null));
    }

    /// <summary>Verifies the timeout interceptor is correctly configured by calling
    /// <see cref="PipelineExtensions.UseTimeout(Pipeline, TimeSpan)"/>. The cancellation token provided to the
    /// invocation pipeline is canceled when the timeout expires.</summary>
    [Test]
    public void Invocation_cancellation_token_is_canceled_after_invocation_timeout_expires()
    {
        // Arrange
        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var pipeline = new Pipeline();
        pipeline.UseTimeout(TimeSpan.FromMilliseconds(500));
        pipeline.Use(next =>
            new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request);
            }));
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = pipeline,
        };

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                null));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
    }

    /// <summary>Verifies that the dispatch deadline has the expected value set by the timeout interceptor.</summary>
    [Test]
    public async Task Invocation_interceptor_sets_the_dispatch_deadline()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var timeout = TimeSpan.FromSeconds(60);
        DateTime deadline = DateTime.MaxValue;
        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher((request, cancel) =>
            {
                deadline = new Dispatch(request).Deadline;
                return new(new OutgoingResponse(request));
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
        });
        var pipeline = new Pipeline();
        pipeline.UseTimeout(timeout);
        var prx = Proxy.FromConnection(connection, "/", invoker: pipeline);

        DateTime expectedDeadline = DateTime.UtcNow + timeout;

        // Act
        await prx.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
            null);

        // Assert
        Assert.That((deadline - expectedDeadline).TotalMilliseconds, Is.LessThan(100));
    }

    /// <summary>Verifies that the invocation timeout value set in the <see cref="Invocation"/> prevails over the
    /// invocation timeout value set with <see cref="PipelineExtensions.UseTimeout(Pipeline, TimeSpan)"/>.</summary>
    [Test]
    public async Task Invocation_timeout_prevails_over_uset_timeout()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var invocationTimeout = TimeSpan.FromSeconds(30);
        DateTime deadline = DateTime.MaxValue;
        await using var server = new Server(new ServerOptions()
        {
            Dispatcher = new InlineDispatcher((request, cancel) =>
            {
                deadline = new Dispatch(request).Deadline;
                return new(new OutgoingResponse(request));
            }),
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport)
        });
        server.Listen();

        await using var connection = new Connection(new ConnectionOptions()
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport)
        });
        var pipeline = new Pipeline();
        pipeline.UseTimeout(TimeSpan.FromSeconds(120));
        var prx = Proxy.FromConnection(connection, "/", invoker: pipeline);

        DateTime expectedDeadline = DateTime.UtcNow + invocationTimeout;

        // Act
        await prx.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
            new Invocation()
            {
                Timeout = invocationTimeout
            });

        // Assert
        Assert.That((deadline - expectedDeadline).TotalMilliseconds, Is.LessThan(100));
    }

    /// <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    /// cannot be canceled.</summary>
    [Test]
    public async Task Invocation_with_infinite_timeout_cannot_be_canceled()
    {
        // Arrange
        var invocation = new Invocation
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        CancellationToken? cancelationToken = null;
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                cancelationToken = cancel;
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await proxy.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
            invocation);

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.False);
    }

    /// <summary>Verifies that setting an invocation deadline requires providing a cancellable cancellation token.
    /// </summary>
    [Test]
    public void Setting_the_invocation_deadline_requires_a_cancellable_cancellation_token()
    {
        // Arrange
        var invocation = new Invocation
        {
            Deadline = DateTime.Now + TimeSpan.FromSeconds(60),
        };

        var proxy = new Proxy(Protocol.IceRpc);

        // Act/Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                invocation,
                cancel: CancellationToken.None));
    }

    /// <summary>Verifies that the timeout interceptor is automatically configured when <see cref="Invocation.Timeout"/> is
    /// set and <see cref="Invocation.Deadline"/> is not.</summary>
    [Test]
    public void Setting_the_invocation_timeout_configures_the_timeout_interceptor()
    {
        // Arrange
        var invocation = new Invocation
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request);
            }),
        };

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                invocation));

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
        Assert.That(hasDeadline, Is.True);
    }
}
