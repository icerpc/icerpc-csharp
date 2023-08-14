// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationEncodingTests
{
    [Test]
    public void Slice2_operation_encode_with_single_parameter()
    {
        // Act
        PipeReader payload = MyOperationsBProxy.Request.EncodeOpInt32(10);

        // Assert
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(5));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_single_parameter()
    {
        // Arrange
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = Encode(10)
        };

        // Act
        int decoded = await IMyOperationsBService.Request.DecodeOpInt32Async(request, default);

        // Assert
        Assert.That(decoded, Is.EqualTo(10));

        static PipeReader Encode(int value)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeSize(5);
            encoder.EncodeInt32(value);
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_single_return()
    {
        // Act
        PipeReader payload = IMyOperationsBService.Response.EncodeOpInt32(10);

        // Assert
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(5));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_single_return()
    {
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = Encode(10)
        };

        int value =
            await MyOperationsBProxy.Response.DecodeOpInt32Async(response, request, InvalidProxy.Instance, default);

        Assert.That(value, Is.EqualTo(10));

        static PipeReader Encode(int value)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeSize(5);
            encoder.EncodeInt32(value);
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_multiple_parameters()
    {
        var payload = MyOperationsBProxy.Request.EncodeOpInt32AndString(10, "hello world!");

        // Assert
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) + 1 (tag end marker)
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(18));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_multiple_parameters()
    {
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = Encode(10, "hello world!")
        };

        (int p1, string p2) = await IMyOperationsBService.Request.DecodeOpInt32AndStringAsync(request, default);

        Assert.That(p1, Is.EqualTo(10));
        Assert.That(p2, Is.EqualTo("hello world!"));

        static PipeReader Encode(int value1, string value2)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) + 1 (tag end marker)
            encoder.EncodeSize(18);
            encoder.EncodeInt32(value1);
            encoder.EncodeString(value2);
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_multiple_return()
    {
        var payload = IMyOperationsBService.Response.EncodeOpInt32AndString(10, "hello world!");

        // Assert
        // readResult: 18 bytes payload + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) + 1 (tag end marker)
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(18));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_multiple_return()
    {
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = Encode(10, "hello world!")
        };

        (int r1, string r2) = await MyOperationsBProxy.Response.DecodeOpInt32AndStringAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo("hello world!"));

        static PipeReader Encode(int value1, string value2)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) + 1 (tag end marker)
            encoder.EncodeSize(18);
            encoder.EncodeInt32(value1);
            encoder.EncodeString(value2);
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_optional_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";

        PipeReader payload = MyOperationsBProxy.Request.EncodeOpOptional(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (bit sequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (optional int 0|4 bytes) + (optional string 0|13 bytes) + 1 (tag end marker)
        int size = 1 + 4 + 13 + (p3 is null ? 0 : 4) + (p4 is null ? 0 : 13) + 1;
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        var bitSequence = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        if (p3 is not null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(p3.Value));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }

        if (p4 is not null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeString(), Is.EqualTo(p4));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_optional_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string r2, int? r3, string? r4) = await IMyOperationsBService.Request.DecodeOpOptionalAsync(request, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            // payload: (bit sequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (optional int 0|4 bytes) + (optional string 0|13 bytes) + 1
            int size = 1 + 4 + 13 + (p3 is null ? 0 : 4) + (p4 is null ? 0 : 13) + 1;
            encoder.EncodeSize(size);
            var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
            encoder.EncodeInt32(p1);
            encoder.EncodeString(p2);
            bitSequenceWriter.Write(p3 is not null);
            if (p3 is not null)
            {
                encoder.EncodeInt32(p3.Value);
            }
            bitSequenceWriter.Write(p4 is not null);
            if (p4 is not null)
            {
                encoder.EncodeString(p4);
            }
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_optional_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";

        PipeReader payload = IMyOperationsBService.Response.EncodeOpOptional(p1, p2, p3, p4);

        // Assert
        // payload: (bit sequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (optional int 0|4 bytes) + (optional string 0|13 bytes) + 1 (tag end marker)
        int size = 1 + 4 + 13 + (p3 is null ? 0 : 4) + (p4 is null ? 0 : 13) + 1;
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        var bitSequence = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        if (p3 is not null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(p3.Value));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }

        if (p4 is not null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeString(), Is.EqualTo(p4));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_optional_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string r2, int? r3, string? r4) =
            await MyOperationsBProxy.Response.DecodeOpOptionalAsync(response, request, InvalidProxy.Instance, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            // payload: (bit sequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (optional int 0|4 bytes) + (optional string 0|13 bytes) + 1 (tag end marker)
            int size = 1 + 4 + 13 + (p3 is null ? 0 : 4) + (p4 is null ? 0 : 13) + 1;
            encoder.EncodeSize(size);
            var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
            encoder.EncodeInt32(p1);
            encoder.EncodeString(p2);
            bitSequenceWriter.Write(p3 is not null);
            if (p3 is not null)
            {
                encoder.EncodeInt32(p3.Value);
            }
            bitSequenceWriter.Write(p4 is not null);
            if (p4 is not null)
            {
                encoder.EncodeString(p4);
            }
            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_tagged_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";

        PipeReader payload = MyOperationsBProxy.Request.EncodeOpTagged(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
        // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes) +
        // 1 for tag end marker
        int size = 4 + 13 + (p3 is null ? 0 : 6) + (p4 is null ? 0 : 18) + 1;
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(p1));
        Assert.That(decoder.DecodeString(), Is.EqualTo(p2));
        if (p3 is not null)
        {
            Assert.That(
                decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(p3));
        }

        if (p4 is not null)
        {
            Assert.That(
                decoder.DecodeTagged(2, (ref SliceDecoder decoder) => decoder.DecodeString()),
                Is.EqualTo(p4));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_tagged_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string r2, int? r3, string? r4) = await IMyOperationsBService.Request.DecodeOpTaggedAsync(request, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
            // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
            // 1 for tag end marker
            int size = 4 + 13 + (p3 is null ? 0 : 6) + (p4 is null ? 0 : 18) + 1;
            encoder.EncodeSize(size);
            encoder.EncodeInt32(p1);
            encoder.EncodeString(p2);
            if (p3 is not null)
            {
                encoder.EncodeTagged(
                    1,
                    size: 4,
                    p3.Value,
                    (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
            }
            if (p4 is not null)
            {
                encoder.EncodeTagged(
                    2,
                    p4,
                    (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            }

            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_tagged_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";

        PipeReader payload = IMyOperationsBService.Response.EncodeOpTagged(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
        // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
        // 1 for tag end marker
        int size = 4 + 13 + (p3 is null ? 0 : 6) + (p4 is null ? 0 : 18) + 1;
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.IsCompleted, Is.True);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(p1));
        Assert.That(decoder.DecodeString(), Is.EqualTo(p2));
        if (p3 is not null)
        {
            Assert.That(
                decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(p3));
        }
        if (p4 is not null)
        {
            Assert.That(
                decoder.DecodeTagged(2, (ref SliceDecoder decoder) => decoder.DecodeString()),
                Is.EqualTo(p4));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(readResult.Buffer.Length));
    }

    [Test]
    public async Task Slice2_operation_decode_with_tagged_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string? r2, int? r3, string? r4) =
            await MyOperationsBProxy.Response.DecodeOpTaggedAsync(response, request, InvalidProxy.Instance, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
            // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
            // 1 for tag end marker
            int size = 4 + 13 + (p3 is null ? 0 : 6) + (p4 is null ? 0 : 18) + 1;
            encoder.EncodeSize(size);
            encoder.EncodeInt32(p1);
            encoder.EncodeString(p2);
            if (p3 is not null)
            {
                encoder.EncodeTagged(
                    1,
                    size: 4,
                    p3.Value,
                    (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
            }

            if (p4 is not null)
            {
                encoder.EncodeTagged(
                    2,
                    p4,
                    (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            }

            encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }
}
