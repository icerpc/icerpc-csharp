// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Configure;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

public partial record struct MyStruct : IMyTrait
{
}

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    [Test]
    public async Task Operation_encode_decode_with_single_parameter()
    {
        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpInt,
            IMyOperations.Request.OpIntAsync,
            10);

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpString,
            IMyOperations.Request.OpStringAsync,
            "hello world!");

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpStringSeq,
            IMyOperations.Request.OpStringSeqAsync,
            new string[] { "hello world!", "bye bye world!" });

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpMyEnum,
            IMyOperations.Request.OpMyEnumAsync,
            MyEnum.enum1);

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpMyException,
            IMyOperations.Request.OpMyExceptionAsync,
            new MyException(1, 2));

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpMyStruct,
            IMyOperations.Request.OpMyStructAsync,
            new MyStruct(1, 2));

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpMyTrait,
            IMyOperations.Request.OpMyTraitAsync,
            new MyStruct(1, 2));

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpService,
            IMyOperations.Request.OpServiceAsync,
            ServicePrx.Parse("icerpc://localhost/hello"));

        await TestSingleParamMethodAsync(
            MyOperationsPrx.Request.OpDict,
            IMyOperations.Request.OpDictAsync,
            new Dictionary<int, string> { [0] = "zero", [1] = "one" });

        static async Task TestSingleParamMethodAsync<T>(
            Func<T, PipeReader> encodeFunc,
            Func<IncomingRequest, CancellationToken, ValueTask<T>> decodeFunc,
            T value)
        {
            await using var connection = new Connection(new ConnectionOptions());

            var request = new IncomingRequest(Protocol.IceRpc)
            {
                Connection = connection,
                Payload = encodeFunc(value)
            };

            T decodedValue = await decodeFunc(request, default);

            Assert.That(value, Is.EqualTo(decodedValue));
        }
    }

    [Test]
    public async Task Operation_encode_decode_with_multiple_parameters()
    {
        await using var connection = new Connection(new ConnectionOptions());

        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpIntAndString(10, "hello world!")
        };

        var decodedValue = await IMyOperations.Request.OpIntAndStringAsync(request, default);

        Assert.That(decodedValue.P1, Is.EqualTo(10));
        Assert.That(decodedValue.P2, Is.EqualTo("hello world!"));
    }
}
