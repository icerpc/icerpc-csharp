// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.RequestContext.Tests;

public sealed class RequestContextMiddlewareTests
{
    [Test]
    public async Task Context_feature_is_set_from_context_field()
    {
        var context = new Dictionary<string, string> { ["Foo"] = "Bar" };
        var proxy = new ServiceAddress(Protocol.IceRpc);

        var pipeReader = EncodeContextField(context);
        ReadOnlySequence<byte> encoded = default;
        if (pipeReader.TryRead(out var readResult))
        {
            encoded = readResult.Buffer;
        }
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
            {
                [RequestFieldKey.Context] = encoded
            }
        };

        IDictionary<string, string>? decoded = null;
        var sut = new RequestContextMiddleware(
           new InlineDispatcher((request, cancellationToken) =>
           {
               decoded = request.Features.Get<IRequestContextFeature>()?.Value;
               return new(new OutgoingResponse(request));
           }));

        await sut.DispatchAsync(request, default);

        pipeReader.Complete();
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded, Is.EqualTo(context));

        static PipeReader EncodeContextField(Dictionary<string, string> context)
        {
            var pipe = new Pipe();
            var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
            encoder.EncodeDictionary(
                context,
                (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            pipe.Writer.Complete();
            return pipe.Reader;
        }
    }
}
