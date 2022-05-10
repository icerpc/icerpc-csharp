// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationGeneratedCodeTests
{
    [Test]
    public async Task Operation_without_parameters_and_void_return()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        Assert.That(async () => await prx.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_from_base_class()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyDerivedOperationsA())
            .BuildServiceProvider();
        var prx = MyDerivedOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        Assert.That(async () => await prx.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        int r = await prx.OpWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_parameters_and_return_values()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        (int r1, int r2) = await prx.OpWithMultipleParametersAndReturnValuesAsync(10, 20);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    [Test]
    public async Task Operation_with_compress_args_and_return_attribute()
    {
        // Arrange
        var pipeline = new Pipeline();
        pipeline.UseDeflate();
        bool compressRequestFeature = false;
        bool compressResponseFeature = false;
        pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
        {
            var response = await next.InvokeAsync(request, cancel);
            compressRequestFeature = request.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes;
            return response;
        }));

        var router = new Router();
        router.UseDeflate();
        router.Use(next => new InlineDispatcher(async (request, cancel) =>
        {
            var response = await next.DispatchAsync(request, cancel);
            compressResponseFeature = request.Features.Get<Features.CompressPayload>() == Features.CompressPayload.Yes;
            return response;
        }));
        router.Map("/", new MyOperationsA());

        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(router)
            .BuildServiceProvider();

        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>(), "/", pipeline);

        // Act
        int r = await prx.OpWithCompressArgsAndReturnAttributeAsync(10);

        // Assert
        Assert.That(r, Is.EqualTo(10));
        Assert.That(compressRequestFeature, Is.True);
        Assert.That(compressResponseFeature, Is.True);
    }

    [Test]
    public async Task Operation_with_byte_stream_argument_and_return()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());
        var data = new byte[] { 1, 2, 3 };
        var pipe = new Pipe();

        // Act
        var invokeTask = prx.OpWithByteStreamArgumentAndReturnAsync(pipe.Reader);
        var flushResult = await pipe.Writer.WriteAsync(data);
        await pipe.Writer.CompleteAsync();
        var reader = await invokeTask;
        var readResult = await reader.ReadAtLeastAsync(data.Length);

        // Assert
        Assert.That(readResult.Buffer.Length, Is.EqualTo(data.Length));
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(data));
        reader.AdvanceTo(readResult.Buffer.End);
    }

    [Test]
    public async Task Operation_with_int_stream_argument_and_return()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        var r = await prx.OpWithIntStreamArgumentAndReturnAsync(GetDataAsync());

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
    public async Task Operation_with_both_regular_and_stream_parameter_and_return()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        (int r1, IAsyncEnumerable<int> r2) =
            await prx.OpWithBothRegularAndStreamParameterAndReturnAsync(10, GetDataAsync());

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
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        Assert.That(
            async () => await prx.OpWithSpecialParameterNamesAsync(
                invocation: 1,
                cancel: 2,
                dispatch: 3),
            Throws.Nothing);
    }

    // TODO check that the parameter has the expected attributes or reject cs::attribute for operation parameters.
    [Test]
    public async Task Operation_with_cs_attribute()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        Assert.That(async () => await prx.OpWithCsAttributeAsync(10), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_return_value_and_encoded_result_attribute()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        var r = await prx.OpWithSingleReturnValueAndEncodedResultAttributeAsync();

        // Assert
        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_return_value_and_encoded_result_attribute()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        (int r1, int r2) = await prx.OpWithMultipleReturnValuesAndEncodedResultAttributeAsync();

        // Assert
        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    class MyOperationsA : Service, IMyOperationsA
    {
        public ValueTask ContinueAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask OpWithoutParametersAndVoidReturnAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<int> OpWithSingleParameterAndReturnValueAsync(
            int p,
            Dispatch dispatch,
            CancellationToken cancel) => new(p);

        public ValueTask<(int R1, int R2)> OpWithMultipleParametersAndReturnValuesAsync(
            int p1,
            int p2,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p2));

        public ValueTask<int> OpWithCompressArgsAndReturnAttributeAsync(
            int p,
            Dispatch dispatch,
            CancellationToken cancel) => new(p);

        public ValueTask<PipeReader> OpWithByteStreamArgumentAndReturnAsync(
            PipeReader p,
            Dispatch dispatch,
            CancellationToken cancel) => new(p);

        public ValueTask<IAsyncEnumerable<int>> OpWithIntStreamArgumentAndReturnAsync(
            IAsyncEnumerable<int> p,
            Dispatch dispatch,
            CancellationToken cancel) => new(p);

        public ValueTask<(int R1, IAsyncEnumerable<int> R2)> OpWithBothRegularAndStreamParameterAndReturnAsync(
            int p1,
            IAsyncEnumerable<int> p2,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p2));

        public ValueTask IdempotentOpAsync(
            Dispatch dispatch,
            CancellationToken cancel) => default;

        public ValueTask OpWithSpecialParameterNamesAsync(
            int invocation,
            int cancel,
            int dispatch,
            Dispatch dispatch_,
            CancellationToken cancel_) => default;

        public ValueTask<int> OpWithCsAttributeAsync(
            int p,
            Dispatch dispatch,
            CancellationToken cancel) => default;

        public ValueTask<IMyOperationsA.OpWithSingleReturnValueAndEncodedResultAttributeEncodedResult> OpWithSingleReturnValueAndEncodedResultAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new(new IMyOperationsA.OpWithSingleReturnValueAndEncodedResultAttributeEncodedResult(10));

        public ValueTask<IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult> OpWithMultipleReturnValuesAndEncodedResultAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new(new IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult(10, 20));
    }

    class MyDerivedOperationsA : MyOperationsA { }
}
