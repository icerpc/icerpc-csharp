// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

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
        Assert.That(async () => await ReadDataAsync(stream), Has.Count.EqualTo(10));

        static async Task<IList<OutputMessage>> ReadDataAsync(IAsyncEnumerable<OutputMessage> stream)
        {
            var result = new List<OutputMessage>();
            await foreach(OutputMessage empty in stream)
            {
                result.Add(empty);
            }
            return result;
        }
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
    }
}
