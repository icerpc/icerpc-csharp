// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.RequestContext.Features;
using IceRpc.Slice;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.RequestContext.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public sealed class RequestContextInterceptorTests
{
    [Test]
    public async Task Context_feature_encoded_in_context_field()
    {
        var context = new Dictionary<string, string> { ["Foo"] = "Bar" };
        var prx = new Proxy(Protocol.IceRpc);
        var request = new OutgoingRequest(prx)
        {
            Features = new FeatureCollection().With<IRequestContextFeature>(
                new RequestContextFeature()
                {
                    Value = context
                })
        };
        Dictionary<string, string>? decoded = null;
        var sut = new RequestContextInterceptor(
           new InlineInvoker((request, cancel) =>
           {
               if (request.Fields.TryGetValue(RequestFieldKey.Context, out OutgoingFieldValue value) &&
                   value.EncodeAction != null)
               {
                   var buffer = new ArrayBufferWriter<byte>();
                   var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
                   value.EncodeAction(ref encoder);

                   var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
                   decoded = decoder.DecodeDictionary(
                       count => new Dictionary<string, string>(count),
                       (ref SliceDecoder decoder) => decoder.DecodeString(),
                       (ref SliceDecoder decoder) => decoder.DecodeString());
               }
               return Task.FromResult(new IncomingResponse(request, request.Connection!));
           }));

        await sut.InvokeAsync(request, default);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded, Is.EqualTo(context));
    }
}
