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
    public void Unary_rpc()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsService());
        var client = new MyOperationsClient(invoker);

        // Act/Assert
        Assert.That(async () => await client.UnaryOpWithEmptyParamAndReturnAsync(new Empty()), Throws.Nothing);
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
        Assert.That(service.Empties, Has.Count.EqualTo(3));

        static async IAsyncEnumerable<Empty> GetDataAsync()
        {
            await Task.Yield();
            yield return new Empty();
            yield return new Empty();
            yield return new Empty();
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
        IAsyncEnumerable<Empty> stream = await client.ServerStreamingOpAsync(new Empty());

        // Assert
        Assert.That(async () => await ReadDataAsync(stream), Has.Count.EqualTo(3));

        static async Task<IList<Empty>> ReadDataAsync(IAsyncEnumerable<Empty> stream)
        {
            var result = new List<Empty>();
            await foreach(Empty empty in stream)
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
        IAsyncEnumerable<Empty> stream = await client.BidirectionalStreamingOpAsync(GetDataAsync());

        // Assert
        Assert.That(service.Empties, Has.Count.EqualTo(3));
        Assert.That(async () => await ReadDataAsync(stream), Has.Count.EqualTo(3));

        static async Task<IList<Empty>> ReadDataAsync(IAsyncEnumerable<Empty> stream)
        {
            var result = new List<Empty>();
            await foreach (Empty empty in stream)
            {
                result.Add(empty);
            }
            return result;
        }

        static async IAsyncEnumerable<Empty> GetDataAsync()
        {
            await Task.Yield();
            yield return new Empty();
            yield return new Empty();
            yield return new Empty();
        }
    }

    [ProtobufService]
    internal partial class MyOperationsService : IMyOperationsService
    {
        public List<Empty> Empties { get; set; } = new List<Empty>();

        public ValueTask<Empty> UnaryOpWithEmptyParamAndReturnAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new Empty());

        public async ValueTask<Empty> ClientStreamingOpAsync(
            IAsyncEnumerable<Empty> stream,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            await foreach (Empty empty in stream)
            {
                Empties.Add(empty);
            }
            return new Empty();
        }

        public ValueTask<IAsyncEnumerable<Empty>> ServerStreamingOpAsync(
            Empty message,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            return new(GetDataAsync());

            async IAsyncEnumerable<Empty> GetDataAsync()
            {
                await Task.Yield();
                yield return message;
                yield return message;
                yield return message;
            }
        }
        public async ValueTask<IAsyncEnumerable<Empty>> BidirectionalStreamingOpAsync(
            IAsyncEnumerable<Empty> stream,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            await foreach (Empty empty in stream)
            {
                Empties.Add(empty);
            }

            return GetDataAsync();

            async IAsyncEnumerable<Empty> GetDataAsync()
            {
                await Task.Yield();
                yield return new Empty();
                yield return new Empty();
                yield return new Empty();
            }
        }
    }
}
