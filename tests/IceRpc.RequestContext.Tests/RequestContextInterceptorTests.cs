// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.RequestContext.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class RequestContextInterceptorTests
{
    [Test]
    public async Task Context_feature_encoded_in_context_field()
    {
        var context = new Dictionary<string, string> { ["Foo"] = "Bar" };
        var proxy = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(proxy)
        {
            Features = new FeatureCollection().With<IRequestContextFeature>(
                new RequestContextFeature()
                {
                    Value = context
                })
        };
        Dictionary<string, string>? decoded = null;
        var sut = new RequestContextInterceptor(
           new InlineInvoker((request, cancellationToken) =>
           {
               if (request.Fields.TryGetValue(RequestFieldKey.Context, out OutgoingFieldValue value) &&
                   value.EncodeAction is not null)
               {
                   var pipe = new Pipe();
                   var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
                   value.EncodeAction(ref encoder);
                   pipe.Writer.Complete();

                   if (pipe.Reader.TryRead(out ReadResult readResult))
                   {
                       var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
                       decoded = decoder.DecodeDictionary(
                           count => new Dictionary<string, string>(count),
                           (ref SliceDecoder decoder) => decoder.DecodeString(),
                           (ref SliceDecoder decoder) => decoder.DecodeString());
                       pipe.Reader.AdvanceTo(readResult.Buffer.End);
                   }
               }
               return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
           }));

        await sut.InvokeAsync(request, default);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded, Is.EqualTo(context));
    }

    [Test]
    public async Task Empty_context_not_encoded_in_context_field()
    {
        var context = new Dictionary<string, string>();
        var proxy = new ServiceAddress(Protocol.IceRpc);
        using var request = new OutgoingRequest(proxy)
        {
            Features = new FeatureCollection().With<IRequestContextFeature>(
                new RequestContextFeature()
                {
                    Value = context
                })
        };

        bool hasContextField = true;
        var sut = new RequestContextInterceptor(
           new InlineInvoker((request, cancellationToken) =>
           {
               hasContextField = request.Fields.ContainsKey(RequestFieldKey.Context);
               return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
           }));
        await sut.InvokeAsync(request, default);

        Assert.That(hasContextField, Is.False);
    }
}
