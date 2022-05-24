// Copyright (c) ZeroC, Inc. All rights reserved.

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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        Assert.That(async () => await prx.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_from_base_class()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyDerivedOperationsA())
            .BuildServiceProvider();
        var prx = MyDerivedOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        Assert.That(async () => await prx.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        int r = await prx.OpWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_parameters_and_return_values()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        (int r1, int r2) = await prx.OpWithMultipleParametersAndReturnValuesAsync(10, 20);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    [Test]
    public async Task Operation_with_byte_stream_argument_and_return()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());
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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
    public async Task Operation_with_string_stream_argument_and_return()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        // Act
        var r = await prx.OpWithStringStreamArgumentAndReturnAsync(GetDataAsync());

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
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyOperationsA())
            .BuildServiceProvider();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        // Act
        (int r1, int r2) = await prx.OpWithMultipleReturnValuesAndEncodedResultAttributeAsync();

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

        PipeReader payload = MyOperationsAPrx.Request.OpReadOnlyMemory(readOnlyMemory);

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryAsync(
                new IncomingRequest(InvalidConnection.IceRpc)
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
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAPrx.Response.OpReadOnlyMemoryAsync(response, request, null, default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing parameter is mapped to a
    /// <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_optional_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        PipeReader payload = MyOperationsAPrx.Request.OpReadOnlyMemoryOptional(new ReadOnlyMemory<int>(p));

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryOptionalAsync(
                new IncomingRequest(InvalidConnection.IceRpc)
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
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAPrx.Response.OpReadOnlyMemoryOptionalAsync(response, request, null, default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing tagged parameter is mapped to
    /// a <see cref="ReadOnlyMemory{T}"/> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_tagged_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        PipeReader payload = MyOperationsAPrx.Request.OpReadOnlyMemoryTagged(new ReadOnlyMemory<int>(p));

        // Assert
        Assert.That(
            async () => await IMyOperationsA.Request.OpReadOnlyMemoryTaggedAsync(
                new IncomingRequest(InvalidConnection.IceRpc)
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
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAPrx.Response.OpReadOnlyMemoryTaggedAsync(response, request, null, default),
            Is.EqualTo(p));
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

        public ValueTask<IAsyncEnumerable<string>> OpWithStringStreamArgumentAndReturnAsync(
            IAsyncEnumerable<string> p,
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
            CancellationToken cancel) => new(new IMyOperationsA.OpWithSingleReturnValueAndEncodedResultAttributeEncodedResult(10, dispatch));

        public ValueTask<IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult> OpWithMultipleReturnValuesAndEncodedResultAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new(new IMyOperationsA.OpWithMultipleReturnValuesAndEncodedResultAttributeEncodedResult(10, 20, dispatch));

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryAsync(
            int[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryOptionalAsync(
            int[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryTaggedAsync(
            int[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    class MyDerivedOperationsA : MyOperationsA { }
}
