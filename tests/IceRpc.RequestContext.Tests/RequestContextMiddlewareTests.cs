// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Codec;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

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
            var encoder = new SliceEncoder(pipe.Writer);
            encoder.EncodeDictionary(
                context,
                (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            pipe.Writer.Complete();
            return pipe.Reader;
        }
    }

    /// <summary>Verifies that a context field with trailing bytes is rejected with
    /// <see cref="InvalidDataException" />.</summary>
    [TestCase("icerpc")]
    [TestCase("ice")]
    public void Context_field_with_trailing_bytes_is_rejected(string protocolName)
    {
        Protocol protocol = Protocol.Parse(protocolName);
        var pipe = new Pipe();
        if (protocol == Protocol.Ice)
        {
            var encoder = new IceEncoder(pipe.Writer);
            encoder.EncodeDictionary(
                new Dictionary<string, string> { ["Foo"] = "Bar" },
                (ref IceEncoder encoder, string key) => encoder.EncodeString(key),
                (ref IceEncoder encoder, string value) => encoder.EncodeString(value));
        }
        else
        {
            var encoder = new SliceEncoder(pipe.Writer);
            encoder.EncodeDictionary(
                new Dictionary<string, string> { ["Foo"] = "Bar" },
                (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
        }
        // Append extra trailing bytes.
        pipe.Writer.GetMemory(4).Span.Clear();
        pipe.Writer.Advance(4);
        pipe.Writer.Complete();
        pipe.Reader.TryRead(out var readResult);

        using var request = new IncomingRequest(protocol, FakeConnectionContext.Instance)
        {
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Context] = readResult.Buffer
            }
        };

        var sut = new RequestContextMiddleware(
            new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request))));

        Assert.That(
            async () => await sut.DispatchAsync(request, default),
            Throws.InstanceOf<InvalidDataException>());

        pipe.Reader.Complete();
    }
}
