// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Configure;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

public partial record struct MyStruct : IMyTrait {}

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    public static IEnumerable<TestCaseData> OperationEncodeDecodeSource
    {
        get
        {
            yield return new TestCaseData(
                (object)MyOperationsPrx.Request.OpInt,
                (object)IMyOperations.Request.OpIntAsync,
                10).SetName("Operation_encode_decode_with_single_parameter(OpInt)");

            yield return new TestCaseData(
                (object)MyOperationsPrx.Request.OpString,
                (object)IMyOperations.Request.OpStringAsync,
                "hello world!").SetName("Operation_encode_decode_with_single_parameter(OpString)");

            yield return new TestCaseData(
                (object)MyOperationsPrx.Request.OpMyStruct,
                (object)IMyOperations.Request.OpMyStructAsync,
                new MyStruct(1, 2)).SetName("Operation_encode_decode_with_single_parameter(OpMyStruct)");

            yield return new TestCaseData(
                (object)MyOperationsPrx.Request.OpMyTrait,
                (object)IMyOperations.Request.OpMyTraitAsync,
                new MyStruct(1, 2)).SetName("Operation_encode_decode_with_single_parameter(OpMyTrait)");
        }
    }

    [Test, TestCaseSource(nameof(OperationEncodeDecodeSource))]
    public async Task Operation_encode_decode_with_single_parameter<T>(
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

    [Test]
    public async Task Operation_encode_decode_with_optional_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hellow world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpOptional(p1, p2, p3, p4)
        };

        var decodedValue = await IMyOperations.Request.OpOptionalAsync(request, default);

        Assert.That(decodedValue.P1, Is.EqualTo(p1));
        Assert.That(decodedValue.P2, Is.EqualTo(p2));
        Assert.That(decodedValue.P3, Is.EqualTo(p3));
        Assert.That(decodedValue.P4, Is.EqualTo(p4));
    }

    [Test]
    public async Task Operation_encode_decode_with_tagged_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hellow world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = MyOperationsPrx.Request.OpTagged(p1, p2, p3, p4)
        };

        var decodedValue = await IMyOperations.Request.OpTaggedAsync(request, default);

        Assert.That(decodedValue.P1, Is.EqualTo(p1));
        Assert.That(decodedValue.P2, Is.EqualTo(p2));
        Assert.That(decodedValue.P3, Is.EqualTo(p3));
        Assert.That(decodedValue.P4, Is.EqualTo(p4));
    }
}
