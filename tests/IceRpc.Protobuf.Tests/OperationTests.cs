// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;
using static System.Net.Mime.MediaTypeNames;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class OperationTests
{
    [Test]
    public async Task Unary_rpc()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsService());
        var client = new MyOperationsClient(invoker);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act
        var response = await client.UnaryOpAsync(message);

        // Assert
        Assert.That(response.P1, Is.EqualTo(message.P1));
        Assert.That(response.P2, Is.EqualTo(message.P2));
    }

    [Test]
    public void Client_streaming_rpc()
    {
        // Arrange
        var service = new MyOperationsService();
        var invoker = new ColocInvoker(service);
        var client = new MyOperationsClient(invoker);

        // Act/Assert
        Assert.That(async () => await client.ClientStreamingOpAsync(GetDataAsync()), Throws.Nothing);
        Assert.That(service.Messages, Has.Count.EqualTo(10));

        async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0 ; i < 10; i++)
            {
                await Task.Yield();
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [Test]
    public async Task Server_streaming_rpc()
    {
        // Arrange
        var service = new MyOperationsService();
        var invoker = new ColocInvoker(service);
        var client = new MyOperationsClient(invoker);

        // Act
        IAsyncEnumerable<OutputMessage> stream = await client.ServerStreamingOpAsync(new Empty());

        // Assert
        var result = new List<OutputMessage>();
        await foreach (OutputMessage empty in stream)
        {
            result.Add(empty);
        }

        Assert.That(result, Has.Count.EqualTo(10));
    }

    [Test]
    public async Task Bidirectional_streaming_rpc()
    {
        // Arrange
        var service = new MyOperationsService();
        var invoker = new ColocInvoker(service);
        var client = new MyOperationsClient(invoker);

        // Act
        IAsyncEnumerable<OutputMessage> stream = await client.BidirectionalStreamingOpAsync(GetDataAsync());

        // Assert
        Assert.That(service.Messages, Has.Count.EqualTo(10));
        Assert.That(async () => await ReadDataAsync(stream), Has.Count.EqualTo(10));

        static async Task<IList<OutputMessage>> ReadDataAsync(IAsyncEnumerable<OutputMessage> stream)
        {
            var result = new List<OutputMessage>();
            await foreach (OutputMessage empty in stream)
            {
                result.Add(empty);
            }
            return result;
        }

        static async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 10; i++)
            {
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [Test]
    public async Task Idempotent_rpc()
    {
        // Arrange
        IDictionary<RequestFieldKey, OutgoingFieldValue>? fields = null;
        var pipeline = new Pipeline();
        pipeline
            .Use(next => new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    var response = await next.InvokeAsync(request, cancellationToken);
                    fields = request.Fields;
                    return response;
                }))
            .Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        // Act
        var response = await client.IdempotentOpAsync(new Empty());

        // Assert
        Assert.That(fields, Is.Not.Null);
        Assert.That(fields, Contains.Key(RequestFieldKey.Idempotent));
    }

    [Test]
    public async Task NoSideEffects_rpc()
    {
        // Arrange
        IDictionary<RequestFieldKey, OutgoingFieldValue>? fields = null;
        var pipeline = new Pipeline();
        pipeline
            .Use(next => new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    var response = await next.InvokeAsync(request, cancellationToken);
                    fields = request.Fields;
                    return response;
                }))
            .Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        // Act
        var response = await client.NoSideEffectsOpAsync(new Empty());

        // Assert
        Assert.That(fields, Is.Not.Null);
        Assert.That(fields, Contains.Key(RequestFieldKey.Idempotent));
    }

    [Test]
    public void Unary_rpc_throws_dispatch_exception()
    {
        // Arrange
        var invoker = new ColocInvoker(new InlineDispatcher(
            (request, cancellationToken) =>
                new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired"))));
        var client = new MyOperationsClient(invoker);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act//Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await client.UnaryOpAsync(message));
        Assert.That(decodedException.StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        Assert.That(decodedException.ConvertToInternalError, Is.True);
    }

    [Test]
    public void Server_streaming_rpc_throws_dispatch_exception()
    {
        // Arrange
        var invoker = new ColocInvoker(new InlineDispatcher(
            (request, cancellationToken) =>
                new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired"))));
        var client = new MyOperationsClient(invoker);

        // Act//Assert
        DispatchException? decodedException = Assert.ThrowsAsync<DispatchException>(
            async () => await client.ServerStreamingOpAsync(new Empty()));
        Assert.That(decodedException.StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        Assert.That(decodedException.ConvertToInternalError, Is.True);
    }

    [Test]
    public async Task Unary_rpc_completes_request_and_response_payloads()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.Payload = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act//Assert
        await client.UnaryOpAsync(message);

        // Assert
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);
    }

    [Test]
    public void Unary_rpc_completes_request_and_response_payloads_upon_failure()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.Payload = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new InlineDispatcher(
                    (request, cancellationToken) =>
                        new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired")))));

        var client = new MyOperationsClient(pipeline);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act//Assert
        Assert.ThrowsAsync<DispatchException>(async () => await client.UnaryOpAsync(message));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);
    }

    [Test]
    public async Task Client_streaming_rpc_completes_request_and_response_payloads()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.PayloadContinuation = requestPayload;
                    var response = await  next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act
        await client.ClientStreamingOpAsync(GetDataAsync());

        // Assert
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);

        static async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 10; i++)
            {
                await Task.Yield();
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [Test]
    public void Client_streaming_rpc_completes_request_and_response_payloads_upon_failure()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.PayloadContinuation = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new InlineDispatcher(
                    (request, cancellationToken) =>
                        new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired")))));

        var client = new MyOperationsClient(pipeline);

        var message = new InputMessage()
        {
            P1 = "P1",
            P2 = 2,
        };

        // Act/Assert
        Assert.ThrowsAsync<DispatchException>(async () => await client.ClientStreamingOpAsync(GetDataAsync()));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);

        static async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 10; i++)
            {
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [Test]
    public async Task Server_streaming_rpc_completes_request_and_response_payloads()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.Payload = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        // Act
        IAsyncEnumerable<OutputMessage> stream = await client.ServerStreamingOpAsync(new Empty());

        // Assert
        var messages = new List<OutputMessage>();
        await foreach (var message in stream)
        {
            messages.Add(message);
        }

        Assert.That(messages, Has.Count.EqualTo(10));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);
    }

    [Test]
    public void Server_streaming_rpc_completes_request_and_response_payloads_upon_failure()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.Payload = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new InlineDispatcher(
                    (request, cancellationToken) =>
                        new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired")))));

        var client = new MyOperationsClient(pipeline);

        // Act
        Assert.ThrowsAsync<DispatchException>(async () => await client.ServerStreamingOpAsync(new Empty()));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);
    }

    [Test]
    public async Task Bidi_streaming_rpc_completes_request_and_response_payloads()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.PayloadContinuation = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new MyOperationsService()));

        var client = new MyOperationsClient(pipeline);

        // Act
        IAsyncEnumerable<OutputMessage> stream = await client.BidirectionalStreamingOpAsync(GetDataAsync());

        // Assert
        var messages = new List<OutputMessage>();
        await foreach (var message in stream)
        {
            messages.Add(message);
        }

        Assert.That(messages.Count, Is.EqualTo(10));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);

        static async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 10; i++)
            {
                await Task.Yield();
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [Test]
    public void Bidi_streaming_rpc_completes_request_and_response_payloads_upon_failure()
    {
        // Arrange
        PayloadPipeReaderDecorator? requestPayload = null;
        PayloadPipeReaderDecorator? responsePayload = null;

        var pipeline = new Pipeline().Use(next =>
            new InlineInvoker(
                async (request, cancellationToken) =>
                {
                    requestPayload = new PayloadPipeReaderDecorator(request.Payload);
                    request.PayloadContinuation = requestPayload;
                    var response = await next.InvokeAsync(request, cancellationToken);
                    responsePayload = new PayloadPipeReaderDecorator(response.Payload);
                    response.Payload = responsePayload;
                    return response;
                })).Into(new ColocInvoker(new InlineDispatcher(
                    (request, cancellationToken) =>
                        new(new OutgoingResponse(request, StatusCode.DeadlineExceeded, "deadline expired")))));

        var client = new MyOperationsClient(pipeline);

        // Act
        Assert.ThrowsAsync<DispatchException>(async () => await client.BidirectionalStreamingOpAsync(GetDataAsync()));
        Assert.That(() => requestPayload!.Completed, Throws.Nothing);
        Assert.That(() => responsePayload!.Completed, Throws.Nothing);

        static async IAsyncEnumerable<InputMessage> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 10; i++)
            {
                await Task.Yield();
                yield return new InputMessage()
                {
                    P1 = $"P{i}",
                    P2 = i,
                };
            }
        }
    }

    [ProtobufService]
    internal partial class MyOperationsService : IMyOperationsService
    {
        public List<InputMessage> Messages { get; set; } = new();

        public ValueTask<OutputMessage> UnaryOpAsync(
            InputMessage message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new OutputMessage()
            {
                P1 = message.P1,
                P2 = message.P2
            });

        public async ValueTask<Empty> ClientStreamingOpAsync(
            IAsyncEnumerable<InputMessage> stream,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            await foreach (InputMessage message in stream)
            {
                Messages.Add(message);
            }
            return new Empty();
        }

        public ValueTask<IAsyncEnumerable<OutputMessage>> ServerStreamingOpAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            return new(GetDataAsync());

            static async IAsyncEnumerable<OutputMessage> GetDataAsync()
            {
                await Task.Yield();
                for (int i = 0; i < 10; i++)
                {
                    yield return new OutputMessage()
                    {
                        P1 = $"P{i}",
                        P2 = i,
                    };
                }
            }
        }
        public async ValueTask<IAsyncEnumerable<OutputMessage>> BidirectionalStreamingOpAsync(
            IAsyncEnumerable<InputMessage> stream,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            await foreach (InputMessage message in stream)
            {
                Messages.Add(message);
            }

            return GetDataAsync();

            static async IAsyncEnumerable<OutputMessage> GetDataAsync()
            {
                await Task.Yield();
                for (int i = 0; i < 10; i++)
                {
                    yield return new OutputMessage()
                    {
                        P1 = $"P{i}",
                        P2 = i,
                    };
                }
            }
        }

        public ValueTask<Empty> IdempotentOpAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new Empty());

        public ValueTask<Empty> NoSideEffectsOpAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new Empty());

        public ValueTask<Empty> DeprecatedOpAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
