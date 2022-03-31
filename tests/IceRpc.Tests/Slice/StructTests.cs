// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class StructTests
{
    public static IEnumerable<MyStruct> MyStructSource =>
        Enumerable.Range(0, 12).Select(x => new MyStruct(x, x * 2)).ToArray();

    [Test, TestCaseSource(nameof(MyStructSource))]
    public async Task Encode_and_decode_my_struct_param(MyStruct expectedValue)
    {
        await using var connection = new Connection(Endpoint.FromString("icerpc://localhost/"));
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = StructOperationsPrx.Request.OpMyStruct(expectedValue)
        };

        MyStruct value = await IStructOperations.Request.OpMyStructAsync(request, default);

        Assert.That(value, Is.EqualTo(expectedValue));
    }

    [Test, TestCaseSource(nameof(MyStructSource))]
    public async Task Encode_and_decode_my_struct_return(MyStruct expectedValue)
    {
        await using var connection = new Connection(Endpoint.FromString("icerpc://localhost/"));
        var response = new IncomingResponse(new OutgoingRequest(Proxy.FromConnection(connection, "/")))
        {
            Connection = connection,
            Payload = IStructOperations.Response.OpMyStruct(SliceEncoding.Slice20, expectedValue)
        };

        MyStruct value = await StructOperationsPrx.Response.OpMyStructAsync(response, default);

        Assert.That(value, Is.EqualTo(expectedValue));
    }
}
