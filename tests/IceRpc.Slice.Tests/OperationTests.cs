// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Derived.Tests;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class OperationTests
{
    [Test]
    public void Operation_without_parameters_and_void_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public void Operation_without_parameters_and_void_return_on_derived_interface()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpDerivedWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public void Operation_from_base_class()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        int r = await proxy.OpWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value_on_derived_interface()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        int r = await proxy.OpDerivedWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_parameters_and_return_values()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        (int r1, int r2) = await proxy.OpWithMultipleParametersAndReturnValuesAsync(10, 20);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    [Test]
    public async Task Operation_with_result_success()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);
        var arg = new Result<string, int>.Success("hello");

        // Act
        Result<string, int> r = await proxy.OpWithResultAsync(arg);

        // Assert
        Assert.That(r, Is.EqualTo(arg));
    }

    [Test]
    public async Task Operation_with_result_failure()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);
        var arg = new Result<string, int>.Failure(123);

        // Act
        Result<string, int> r = await proxy.OpWithResultAsync(arg);

        // Assert
        Assert.That(r, Is.EqualTo(arg));
    }

    [Test]
    public async Task Operation_with_byte_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        var data = new byte[] { 1, 2, 3 };
        var pipe = new Pipe();

        // Act
        var invokeTask = proxy.OpWithByteStreamArgumentAndReturnAsync(pipe.Reader);
        _ = await pipe.Writer.WriteAsync(data);
        pipe.Writer.Complete();
        var reader = await invokeTask;
        var readResult = await reader.ReadAtLeastAsync(data.Length);

        // Assert
        Assert.That(readResult.Buffer.Length, Is.EqualTo(data.Length));
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(data));
        reader.AdvanceTo(readResult.Buffer.End);
        reader.Complete();
    }

    [Test]
    public async Task Operation_with_optional_byte_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var stream = await proxy.OpWithOptionalByteStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = stream.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(2));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(3));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<byte?> GetDataAsync()
        {
            await Task.Yield();
            yield return 1;
            yield return null;
            yield return 2;
            yield return null;
            yield return 3;
            yield return null;
        }
    }

    [Test]
    public async Task Operation_with_int_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var stream = await proxy.OpWithIntStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = stream.GetAsyncEnumerator();
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
    public async Task Operation_with_optional_int_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var stream = await proxy.OpWithOptionalIntStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = stream.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(2));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(3));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<int?> GetDataAsync()
        {
            await Task.Yield();
            yield return 1;
            yield return null;
            yield return 2;
            yield return null;
            yield return 3;
            yield return null;
        }
    }

    [Test]
    public async Task Operation_with_string_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var stream = await proxy.OpWithStringStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = stream.GetAsyncEnumerator();
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
    public async Task Operation_with_optional_string_stream_argument_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var stream = await proxy.OpWithOptionalStringStreamArgumentAndReturnAsync(GetDataAsync());

        // Assert
        var enumerator = stream.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 1"));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 2"));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("hello world 3"));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.Null);

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<string?> GetDataAsync()
        {
            await Task.Yield();
            yield return "hello world 1";
            yield return null;
            yield return "hello world 2";
            yield return null;
            yield return "hello world 3";
            yield return null;
        }
    }

    [Test]
    public async Task Operation_with_both_regular_and_stream_parameter_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

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
    public void Operation_with_special_parameter_names()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(
            async () => await proxy.OpWithSpecialParameterNamesAsync(
                cancel: 1,
                features: 2),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_return_value_and_encoded_return_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var r = await proxy.OpWithSingleReturnValueAndEncodedReturnAttributeAsync();

        // Assert
        Assert.That(r, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Operation_with_multiple_return_value_and_encoded_return_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        (int[] r1, int[] r2) = await proxy.OpWithMultipleReturnValuesAndEncodedReturnAttributeAsync();

        // Assert
        Assert.That(r1, Is.EqualTo(new int[] { 1, 2, 3 }));
        Assert.That(r2, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Operation_with_stream_return_value_and_encoded_return_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        (int[] r1, IAsyncEnumerable<int> r2) = await proxy.OpWithStreamReturnAndEncodedReturnAttributeAsync();

        // Assert
        Assert.That(r1, Is.EqualTo(new int[] { 1, 2, 3 }));
        var enumerator = r2.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(2));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(3));

        Assert.That(await enumerator.MoveNextAsync(), Is.False);
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing parameter is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_param()
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader payload = MyOperationsAProxy.Request.EncodeOpReadOnlyMemory(readOnlyMemory);

        // Assert
        Assert.That(
            async () => await IMyOperationsAService.Request.DecodeOpReadOnlyMemoryAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing return value is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_return()
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader payload = IMyOperationsAService.Response.EncodeOpReadOnlyMemory(readOnlyMemory);

        // Assert
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.DecodeOpReadOnlyMemoryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing parameter is mapped to a
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_optional_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        PipeReader payload = MyOperationsAProxy.Request.EncodeOpReadOnlyMemoryOptional(new ReadOnlyMemory<int>(p));

        // Act/Assert
        Assert.That(
            async () => await IMyOperationsAService.Request.DecodeOpReadOnlyMemoryOptionalAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing optional return value is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the optional incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_optional_return(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(p);

        // Act
        PipeReader payload = IMyOperationsAService.Response.EncodeOpReadOnlyMemoryOptional(readOnlyMemory);

        // Assert
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.DecodeOpReadOnlyMemoryOptionalAsync(
                response,
                request,
                InvalidProxy.Instance,
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing tagged parameter is mapped to
    /// a <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_tagged_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        PipeReader payload = MyOperationsAProxy.Request.EncodeOpReadOnlyMemoryTagged(new ReadOnlyMemory<int>(p));

        // Act/Assert
        Assert.That(
            async () => await IMyOperationsAService.Request.DecodeOpReadOnlyMemoryTaggedAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing tagged return value is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the optional incoming return value is not affected.</summary>
    [Test]
    public void Slice2_operation_encode_with_readonly_memory_tagged_return(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(p);

        // Act
        PipeReader payload = IMyOperationsAService.Response.EncodeOpReadOnlyMemoryTagged(readOnlyMemory);

        // Assert
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.DecodeOpReadOnlyMemoryTaggedAsync(
                response,
                request,
                InvalidProxy.Instance,
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that tagged parameters are correctly skipped. The interface MyTaggedOperationsV0 doesn't
    /// support any of the tagged parameters used by MyTaggedOperations interface.</summary>
    [Test]
    public void Skip_tagged_parameters()
    {
        // Arrange
        var service = new MyTaggedOperationsV0Service();
        var invoker = new ColocInvoker(service);
        var proxy = new MyTaggedOperationsProxy(invoker);

        // Act/Assert
        Assert.That(() => proxy.OpAsync(10, 1, 12), Throws.Nothing);
    }

    [Test]
    public async Task Proxy_decoded_from_incoming_response_has_the_invoker_of_the_proxy_that_sent_the_request()
    {
        // Arrange
        var service = new MyOperationsAService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        PingableProxy receivedProxy = await proxy.OpWithProxyReturnValueAsync();

        // Assert
        Assert.That(receivedProxy.Invoker, Is.EqualTo(((IProxy)proxy).Invoker));
    }

    [Test]
    public async Task Proxy_decoded_from_incoming_request_has_invalid_invoker()
    {
        // Arrange
        var service = new MyOperationsAService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        await proxy.OpWithProxyParameterAsync(PingableProxy.FromPath("/hello"));

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.EqualTo(InvalidInvoker.Instance));
    }

    [Test]
    public void Call_operation_defined_in_base_interface_defined_in_non_parent_module()
    {
        // Arrange
        var service = new MyDerivedOperationsService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyDerivedOperationsProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpAsync(), Throws.Nothing);
    }

    [Test]
    public void Operation_with_trailing_optionals_and_stream_requires_all_parameters()
    {
        // Arrange
        var service = new MyOperationsAService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(() => proxy.OpWithTrailingOptionalValuesAndStreamAsync(0, null, 1, null, null, GetDataAsync()), Throws.Nothing);

        static async IAsyncEnumerable<byte?> GetDataAsync()
        {
            await Task.Yield();
            yield return 1;
            yield return null;
            yield return 2;
            yield return null;
            yield return 3;
            yield return null;
        }
    }

    [SliceService]
    private partial class MyOperationsAService : IMyOperationsAService
    {
        public PingableProxy? ReceivedProxy;

        public ValueTask ContinueAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask OpWithoutParametersAndVoidReturnAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<int> OpWithSingleParameterAndReturnValueAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<(int R1, int R2)> OpWithMultipleParametersAndReturnValuesAsync(
            int p1,
            int p2,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((p1, p2));

        public ValueTask<Result<string, int>> OpWithResultAsync(
            Result<string, int> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<int> OpWithCompressArgsAndReturnAttributeAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<PipeReader> OpWithByteStreamArgumentAndReturnAsync(
            PipeReader p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<IAsyncEnumerable<byte?>> OpWithOptionalByteStreamArgumentAndReturnAsync(
            IAsyncEnumerable<byte?> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<IAsyncEnumerable<int>> OpWithIntStreamArgumentAndReturnAsync(
            IAsyncEnumerable<int> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<IAsyncEnumerable<int?>> OpWithOptionalIntStreamArgumentAndReturnAsync(
            IAsyncEnumerable<int?> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<IAsyncEnumerable<string>> OpWithStringStreamArgumentAndReturnAsync(
            IAsyncEnumerable<string> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<IAsyncEnumerable<string?>> OpWithOptionalStringStreamArgumentAndReturnAsync(
            IAsyncEnumerable<string?> p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<(int R1, IAsyncEnumerable<int> R2)> OpWithBothRegularAndStreamParameterAndReturnAsync(
            int p1,
            IAsyncEnumerable<int> p2,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((p1, p2));

        public ValueTask IdempotentOpAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => default;

        public ValueTask OpWithSpecialParameterNamesAsync(
            int cancel,
            int features,
            IFeatureCollection features_,
            CancellationToken cancellationToken) => default;

        public ValueTask<PipeReader> OpWithSingleReturnValueAndEncodedReturnAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(IMyOperationsAService.Response.EncodeOpWithSingleReturnValueAndEncodedReturnAttribute(
                new int[] { 1, 2, 3 },
                features.Get<ISliceFeature>()?.EncodeOptions));

        public ValueTask<PipeReader> OpWithMultipleReturnValuesAndEncodedReturnAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(IMyOperationsAService.Response.EncodeOpWithMultipleReturnValuesAndEncodedReturnAttribute(
                new int[] { 1, 2, 3 },
                new int[] { 1, 2, 3 },
                features.Get<ISliceFeature>()?.EncodeOptions));

        public ValueTask<(PipeReader Payload, IAsyncEnumerable<int> R2)> OpWithStreamReturnAndEncodedReturnAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            var payload = IMyOperationsAService.Response.EncodeOpWithStreamReturnAndEncodedReturnAttribute(
                new int[] { 1, 2, 3 },
                features.Get<ISliceFeature>()?.EncodeOptions);
            return new((payload, GetDataAsync()));

            static async IAsyncEnumerable<int> GetDataAsync()
            {
                await Task.Yield();
                yield return 1;
                yield return 2;
                yield return 3;
            }
        }

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryAsync(
            int[] p1,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryOptionalAsync(
            int[]? p1,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryTaggedAsync(
            int[]? p1,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p1);

        public ValueTask OpWithProxyParameterAsync(
            PingableProxy service,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            ReceivedProxy = service;
            return default;
        }

        public ValueTask<PingableProxy> OpWithProxyReturnValueAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(PingableProxy.FromPath("/hello"));
        public ValueTask OpWithTrailingOptionalValuesAsync(int p1, int? p2, int p3, int? p4, int? p5, IFeatureCollection features, CancellationToken cancellationToken) => default;
        public ValueTask OpWithTrailingOptionalValuesAndStreamAsync(int p1, int? p2, int p3, int? p4, int? p5, IAsyncEnumerable<byte?> p6, IFeatureCollection features, CancellationToken cancellationToken) => default;
    }

    [SliceService]
    private sealed partial class MyDerivedOperationsAService : MyOperationsAService, IMyDerivedOperationsAService
    {
        public ValueTask OpDerivedWithoutParametersAndVoidReturnAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => default;

        public ValueTask<int> OpDerivedWithSingleParameterAndReturnValueAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);
    }

    [SliceService]
    private sealed partial class MyTaggedOperationsV0Service : IMyTaggedOperationsV0Service
    {
        public ValueTask OpAsync(int y, IFeatureCollection features, CancellationToken cancellationToken) => default;
    }

    [SliceService]
    private sealed partial class MyDerivedOperationsService : IMyDerivedOperationsService
    {
        public ValueTask OpAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
        public ValueTask OpDerivedAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }
}
