// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.RequestContext.Features;
using IceRpc.Slice;
using IceRpc.Tests;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.RequestContext.Tests;

[Timeout(5000)]
public sealed class RequestContextMiddlewareTests
{
    [Test]
    public async Task Context_feature_is_set_from_context_field()
    {
        var context = new Dictionary<string, string> { ["Foo"] = "Bar" };
        var prx = new Proxy(Protocol.IceRpc);
        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.Context] = EncodeContextField(context)
            }
        };

        IDictionary<string, string>? decoded = null;
        var sut = new RequestContextMiddleware(
           new InlineDispatcher((request, cancel) =>
           {
               decoded = request.Features.Get<IRequestContextFeature>()?.Value;
               return new(new OutgoingResponse(request));
           }));

        await sut.DispatchAsync(request, default);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded, Is.EqualTo(context));

        ReadOnlySequence<byte> EncodeContextField(Dictionary<string, string> context)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
            encoder.EncodeDictionary(
                context,
                (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            return new ReadOnlySequence<byte>(buffer.WrittenMemory);
        }
    }
}
