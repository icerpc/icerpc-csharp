// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    [Test]
    public async Task Operation_without_parameters_and_void_return()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_from_base_class()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyDerivedOperationsA())
            .AddIceRpcProxy<IMyDerivedOperationsAProxy, MyDerivedOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyDerivedOperationsAProxy proxy = provider.GetRequiredService<IMyDerivedOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        int r = await proxy.OpWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_parameters_and_return_values()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        (int r1, int r2) = await proxy.OpWithMultipleParametersAndReturnValuesAsync(10, 20);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    [Test]
    public async Task Operation_with_byte_stream_argument_and_return()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        var data = new byte[] { 1, 2, 3 };
        var pipe = new Pipe();

        // Act
        var invokeTask = proxy.OpWithByteStreamArgumentAndReturnAsync(pipe.Reader);
        var flushResult = await pipe.Writer.WriteAsync(data);
        await pipe.Writer.CompleteAsync();
        var reader = await invokeTask;
        var readResult = await reader.ReadAtLeastAsync(data.Length);

        // Assert
        Assert.That(readResult.Buffer.Length, Is.EqualTo(data.Length));
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(data));
        reader.AdvanceTo(readResult.Buffer.End);
        await reader.CompleteAsync();
    }

    [Test]
    public async Task Operation_with_int_stream_argument_and_return()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        var r = await proxy.OpWithIntStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = r.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(2));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(3));

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<int> GetDataAsync()
        {
            await Task.Yield();
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    [Test]
    public async Task Operation_with_string_stream_argument_and_return()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        var r = await proxy.OpWithStringStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = r.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 1"));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 2"));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 3"));

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<string> GetDataAsync()
        {
            await Task.Yield();
            yield return "hello world 1";
            yield return "hello world 2";
            yield return "hello world 3";
        }
    }

    [Test]
    public async Task Operation_with_both_regular_and_stream_parameter_and_return()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        (int r1, IAsyncEnumerable<int> r2) =
            await proxy.OpWithBothRegularAndStreamParameterAndReturnAsync(10, GetDataAsync());

        // Assert
        Assert.That(r1, Is.EqualTo(10));

        var enumerator = r2.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(2));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(3));

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<int> GetDataAsync()
        {
            await Task.Yield();
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    [Test]
    public async Task Operation_with_special_parameter_names()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        Assert.That(
            async () => await proxy.OpWithSpecialParameterNamesAsync(
                cancel: 1,
                features: 2),
            Throws.Nothing);
    }

    // TODO check that the parameter has the expected attributes or reject cs::attribute for operation parameters.
    [Test]
    public async Task Operation_with_cs_attribute()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        Assert.That(async () => await proxy.OpWithCsAttributeAsync(10), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_return_value_and_encoded_result_attribute()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        var r = await proxy.OpWithSingleReturnValueAndEncodedResultAttributeAsync();

        // Assert
        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_return_value_and_encoded_result_attribute()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyOperationsA())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act
        (int r1, int r2) = await proxy.OpWithMultipleReturnValuesAndEncodedResultAttributeAsync();

        // Assert
        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing parameter is mapped to
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_param()
    {
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        PipeReader payload = MyOperationsAProxy.Request.OpReadOnlyMemory(readOnlyMemory);

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryAsync(
                new IncomingRequest(FakeConnectionContext.IceRpc)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing return value is mapped to
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_return()
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader payload = IMyOperationsA.Response.OpReadOnlyMemory(readOnlyMemory);

        // Assert
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.OpReadOnlyMemoryAsync(
                response,
                request,
                new ServiceProxy(NotImplementedInvoker.Instance),
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing parameter is mapped to a
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_optional_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        PipeReader payload = MyOperationsAProxy.Request.OpReadOnlyMemoryOptional(new ReadOnlyMemory<int>(p));

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryOptionalAsync(
                new IncomingRequest(FakeConnectionContext.IceRpc)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing optional return value is mapped to
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the optional incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_optional_return(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(p);

        // Act
        PipeReader payload = IMyOperationsA.Response.OpReadOnlyMemoryOptional(readOnlyMemory);

        // Assert
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.OpReadOnlyMemoryOptionalAsync(
                response,
                request,
                new ServiceProxy(NotImplementedInvoker.Instance),
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing tagged parameter is mapped to
    /// a <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_tagged_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        PipeReader payload = MyOperationsAProxy.Request.OpReadOnlyMemoryTagged(new ReadOnlyMemory<int>(p));

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryTaggedAsync(
                new IncomingRequest(FakeConnectionContext.IceRpc)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing tagged return value is mapped to
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the optional incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_tagged_return(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(p);

        // Act
        PipeReader payload = IMyOperationsA.Response.OpReadOnlyMemoryTagged(readOnlyMemory);

        // Assert
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.OpReadOnlyMemoryTaggedAsync(
                response,
                request,
                new ServiceProxy(NotImplementedInvoker.Instance),
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that tagged parameters has a default value that is equivalent to a non set tagged parameter.
    /// </summary>
    [Test]
    public async Task Tagged_default_values()
    {
        var service = new MyTaggedOperations();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(service)
            .AddIceRpcProxy<IMyTaggedOperationsProxy, MyTaggedOperationsProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyTaggedOperationsProxy proxy = provider.GetRequiredService<IMyTaggedOperationsProxy>();
        provider.GetRequiredService<Server>().Listen();

        await proxy.OpAsync(1, z: 10);

        Assert.That(service.X, Is.EqualTo(1));
        Assert.That(service.Y, Is.Null);
        Assert.That(service.Z, Is.EqualTo(10));
    }

    /// <summary>Verifies that a tagged sequence parameter that uses the <see cref="ReadOnlyMemory{T}"/> mapping has a
    /// default value that is equivalent to a non set tagged parameter.</summary>
    [Test]
    public async Task Proxy_tagged_default_values_with_readonly_memory_params()
    {
        var service = new MyTaggedOperationsReadOnlyMemoryParams();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(service)
            .AddIceRpcProxy<IMyTaggedOperationsReadOnlyMemoryParamsProxy, MyTaggedOperationsReadOnlyMemoryParamsProxy>()
            .BuildServiceProvider(validateScopes: true);

        IMyTaggedOperationsReadOnlyMemoryParamsProxy proxy =
            provider.GetRequiredService<IMyTaggedOperationsReadOnlyMemoryParamsProxy>();
        provider.GetRequiredService<Server>().Listen();

        await proxy.OpAsync(new int[] { 1 }, z: new int[] { 10 });

        Assert.That(service.X, Is.EqualTo(new int[] { 1 }));
        Assert.That(service.Y, Is.Null);
        Assert.That(service.Z, Is.EqualTo(new int[] { 10 }));
    }

    class MyOperationsA : Service, IMyOperationsA
    {
        public ValueTask ContinueAsync(IFeatureCollection features, CancellationToken cancel) => default;

        public ValueTask OpWithoutParametersAndVoidReturnAsync(IFeatureCollection features, CancellationToken cancel) => default;

        public ValueTask<int> OpWithSingleParameterAndReturnValueAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);

        public ValueTask<(int R1, int R2)> OpWithMultipleParametersAndReturnValuesAsync(
            int p1,
            int p2,
            IFeatureCollection features,
            CancellationToken cancel) => new((p1, p2));

        public ValueTask<int> OpWithCompressArgsAndReturnAttributeAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);

        public ValueTask<PipeReader> OpWithByteStreamArgumentAndReturnAsync(
            PipeReader p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);

        public ValueTask<IAsyncEnumerable<int>> OpWithIntStreamArgumentAndReturnAsync(
            IAsyncEnumerable<int> p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);

        public ValueTask<IAsyncEnumerable<string>> OpWithStringStreamArgumentAndReturnAsync(
            IAsyncEnumerable<string> p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);

        public ValueTask<(int R1, IAsyncEnumerable<int> R2)> OpWithBothRegularAndStreamParameterAndReturnAsync(
            int p1,
            IAsyncEnumerable<int> p2,
            IFeatureCollection features,
            CancellationToken cancel) => new((p1, p2));

        public ValueTask IdempotentOpAsync(
            IFeatureCollection features,
            CancellationToken cancel) => default;

        public ValueTask OpWithSpecialParameterNamesAsync(
            int cancel,
            int features,
            IFeatureCollection features_,
            CancellationToken cancel_) => default;

        public ValueTask<int> OpWithCsAttributeAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancel) => default;

        public ValueTask<IMyOperationsA.OpWithSingleReturnValueAndEncodedResultAttributeEncodedResult> OpWithSingleReturnValueAndEncodedResultAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancel) => new(new IMyOperationsA.OpWithSingleReturnValueAndEncodedResultAttributeEncodedResult(10, features));

        public ValueTask<IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult> OpWithMultipleReturnValuesAndEncodedResultAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancel) => new(new IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult(10, 20, features));

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryAsync(
            int[] p1,
            IFeatureCollection features,
            CancellationToken cancel) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryOptionalAsync(
            int[]? p1,
            IFeatureCollection features,
            CancellationToken cancel) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryTaggedAsync(
            int[]? p1,
            IFeatureCollection features,
            CancellationToken cancel) => new(p1);
    }

    private class MyDerivedOperationsA : MyOperationsA { }

    private class MyTaggedOperations : Service, IMyTaggedOperations
    {
        internal int X { get; set; }
        internal int? Y { get; set; }
        internal int? Z { get; set; }

        public ValueTask OpAsync(int x, int? y, int? z, IFeatureCollection features, CancellationToken cancel)
        {
            X = x;
            Y = y;
            Z = z;
            return default;
        }
    }

    private class MyTaggedOperationsReadOnlyMemoryParams : Service, IMyTaggedOperationsReadOnlyMemoryParams
    {
        internal int[] X { get; set; } = Array.Empty<int>();
        internal int[]? Y { get; set; }
        internal int[]? Z { get; set; }

        public ValueTask OpAsync(int[] x, int[]? y, int[]? z, IFeatureCollection features, CancellationToken cancel)
        {
            X = x;
            Y = y;
            Z = z;
            return default;
        }
    }
}
