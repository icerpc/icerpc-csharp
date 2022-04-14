// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    [Test]
    public void Slice2_operation_encode_with_single_parameter()
    {
        // Act
        PipeReader payload = MyOperationsPrx.Request.OpInt(10);

        // Assert
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(8));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(4));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
    }

    [Test]
    public async Task Slice2_operation_decode_with_single_parameter()
    {
        // Arrange
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = Encode(10)
        };

        // Act
        int decoded = await IMyOperations.Request.OpIntAsync(request, default);

        // Assert
        Assert.That(decoded, Is.EqualTo(10));

        static PipeReader Encode(int value)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(value);
            SliceEncoder.EncodeVarULong(4, sizePlaceHolder);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_single_return()
    {
        // Act
        PipeReader payload = IMyOperations.Response.OpInt(10);

        // Assert
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(8));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(4));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
    }

    [Test]
    public async Task Slice2_operation_decode_with_single_return()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = Encode(10)
        };

        int value = await MyOperationsPrx.Response.OpIntAsync(response, default);

        Assert.That(value, Is.EqualTo(10));

        static PipeReader Encode(int value)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(value);
            SliceEncoder.EncodeVarULong(4, sizePlaceHolder);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void Slice2_operation_encode_with_multiple_parameters()
    {
        var payload = MyOperationsPrx.Request.OpIntAndString(10, "hello world!");

        // Assert
        // readResult: 17 bytes payload + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents)
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(21));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(17));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
    }

    [Test]
    public async Task Slice2_operation_decode_with_multiple_parameters()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = Encode(10, "hello world!")
        };

        var decoded = await IMyOperations.Request.OpIntAndStringAsync(request, default);

        Assert.That(decoded.P1, Is.EqualTo(10));
        Assert.That(decoded.P2, Is.EqualTo("hello world!"));

        static PipeReader Encode(int value1, string value2)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(value1);
            encoder.EncodeString(value2);
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents)
            SliceEncoder.EncodeVarULong(17, sizePlaceHolder);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }

    [Test]
    public void  Slice2_operation_encode_with_multiple_return()
    {
        var payload = IMyOperations.Response.OpIntAndString(10, "hello world!");

        // Assert
        // readResult: 17 bytes payload + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents)
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(21));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(17));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
    }

    [Test]
    public async Task Slice2_operation_decode_with_multiple_return()
    {
        await using var connection = new Connection(new ConnectionOptions());
        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = Encode(10, "hello world!")
        };

        (int r1, string r2) = await MyOperationsPrx.Response.OpIntAndStringAsync(response, default);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo("hello world!"));

        static PipeReader Encode(int value1, string value2)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(value1);
            encoder.EncodeString(value2);
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents)
            SliceEncoder.EncodeVarULong(17, sizePlaceHolder);
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

        PipeReader payload = MyOperationsPrx.Request.OpOptional(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (bitsequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (optional int 0|4 bytes) + (optional string 0|13 bytes)
        int size = 1 + 4 + 13 + (p3 == null ? 0 : 4) + (p4 == null ? 0 : 13);
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(size + 4));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(size));
        var bitSequence = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        if (p3 != null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeInt(), Is.EqualTo(p3.Value));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }

        if (p4 != null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeString(), Is.EqualTo(p4));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }
    }

    [Test]
    public async Task Slice2_operation_decode_with_optional_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string r2, int? r3, string? r4) = await IMyOperations.Request.OpOptionalAsync(request, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
            encoder.EncodeInt(p1);
            encoder.EncodeString(p2);
            bitSequenceWriter.Write(p3 != null);
            if (p3 != null)
            {
                encoder.EncodeInt(p3.Value);
            }

            bitSequenceWriter.Write(p4 != null);
            if (p4 != null)
            {
                encoder.EncodeString(p4);
            }
            // payload: (bitsequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (optional int 0|4 bytes) + (optional string 0|13 bytes)
            int size = 1 + 4 + 13 + (p3 == null ? 0 : 4) + (p4 == null ? 0 : 13);
            SliceEncoder.EncodeVarULong((ulong)size, sizePlaceHolder);
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

        PipeReader payload = IMyOperations.Response.OpOptional(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (bitsequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (optional int 0|4 bytes) + (optional string 0|13 bytes)
        int size = 1 + 4 + 13 + (p3 == null ? 0 : 4) + (p4 == null ? 0 : 13);
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(size + 4));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(size));
        var bitSequence = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeString(), Is.EqualTo("hello world!"));
        if (p3 != null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeInt(), Is.EqualTo(p3.Value));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }

        if (p4 != null)
        {
            Assert.That(bitSequence.Read(), Is.True);
            Assert.That(decoder.DecodeString(), Is.EqualTo(p4));
        }
        else
        {
            Assert.That(bitSequence.Read(), Is.False);
        }
    }

    [Test]
    public async Task Slice2_operation_decode_with_optional_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = Encode(p1, p2, p3, p4)
        };

        var value = await MyOperationsPrx.Response.OpOptionalAsync(response, default);

        Assert.That(value.R1, Is.EqualTo(p1));
        Assert.That(value.R2, Is.EqualTo(p2));
        Assert.That(value.R3, Is.EqualTo(p3));
        Assert.That(value.R4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
            encoder.EncodeInt(p1);
            encoder.EncodeString(p2);
            bitSequenceWriter.Write(p3 != null);
            if (p3 != null)
            {
                encoder.EncodeInt(p3.Value);
            }

            bitSequenceWriter.Write(p4 != null);
            if (p4 != null)
            {
                encoder.EncodeString(p4);
            }
            // payload: (bitsequence 1 byte) (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (optional int 0|4 bytes) + (optional string 0|13 bytes)
            int size = 1 + 4 + 13 + (p3 == null ? 0 : 4) + (p4 == null ? 0 : 13);
            SliceEncoder.EncodeVarULong((ulong)size, sizePlaceHolder);
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

        PipeReader payload = MyOperationsPrx.Request.OpTagged(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
        // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
        int size = 4 + 13 + (p3 == null ? 0 : 6) + (p4 == null ? 0 : 18);
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(size + 4));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(size));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(p1));
        Assert.That(decoder.DecodeString(), Is.EqualTo(p2));
        if (p3 != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    1,
                    TagFormat.F4,
                    (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(p3));
        }

        if (p4 != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    2,
                    TagFormat.OVSize,
                    (ref SliceDecoder decoder) => decoder.DecodeString()),
                Is.EqualTo(p4));
        }
    }

    [Test]
    public async Task Slice2_operation_decode_with_tagged_parameters(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var request = new IncomingRequest(Protocol.IceRpc)
        {
            Connection = connection,
            Payload = Encode(p1, p2, p3, p4)
        };

        (int r1, string r2, int? r3, string? r4) = await IMyOperations.Request.OpTaggedAsync(request, default);

        Assert.That(r1, Is.EqualTo(p1));
        Assert.That(r2, Is.EqualTo(p2));
        Assert.That(r3, Is.EqualTo(p3));
        Assert.That(r4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(p1);
            encoder.EncodeString(p2);
            if (p3 != null)
            {
                encoder.EncodeTagged(
                    1,
                    TagFormat.F4,
                    size: 4,
                    p3.Value,
                    (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
            }

            if (p4 != null)
            {
                encoder.EncodeTagged(
                    2,
                    TagFormat.OVSize,
                    p4,
                    (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            }
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
            // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
            int size = 4 + 13 + (p3 == null ? 0 : 6) + (p4 == null ? 0 : 18);
            SliceEncoder.EncodeVarULong((ulong)size, sizePlaceHolder);
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

        PipeReader payload = IMyOperations.Response.OpTagged(p1, p2, p3, p4);

        // Assert
        // readResult: size + 4 bytes payload size
        // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
        // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
        // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
        int size = 4 + 13 + (p3 == null ? 0 : 6) + (p4 == null ? 0 : 18);
        Assert.That(payload.TryRead(out var readResult));
        Assert.That(readResult.Buffer.Length, Is.EqualTo(size + 4));
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeVarULong(), Is.EqualTo(size));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(p1));
        Assert.That(decoder.DecodeString(), Is.EqualTo(p2));
        if (p3 != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    1,
                    TagFormat.F4,
                    (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(p3));
        }

        if (p4 != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    2,
                    TagFormat.OVSize,
                    (ref SliceDecoder decoder) => decoder.DecodeString()),
                Is.EqualTo(p4));
        }
    }

    [Test]
    public async Task Operation_encode_decode_with_tagged_return(
        [Values(10, null)] int? p3,
        [Values("hello world!", null)] string? p4)
    {
        const int p1 = 10;
        const string p2 = "hello world!";
        await using var connection = new Connection(new ConnectionOptions());
        var response = new IncomingResponse(new OutgoingRequest(new Proxy(Protocol.IceRpc)))
        {
            Connection = connection,
            Payload = Encode(p1, p2, p3, p4)
        };

        var value = await MyOperationsPrx.Response.OpTaggedAsync(response, default);

        Assert.That(value.R1, Is.EqualTo(p1));
        Assert.That(value.R2, Is.EqualTo(p2));
        Assert.That(value.R3, Is.EqualTo(p3));
        Assert.That(value.R4, Is.EqualTo(p4));

        static PipeReader Encode(int p1, string p2, int? p3, string? p4)
        {
            var bufferWriter = new MemoryBufferWriter(new byte[256]);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            Span<byte> sizePlaceHolder = encoder.GetPlaceholderSpan(4);
            encoder.EncodeInt(p1);
            encoder.EncodeString(p2);
            if (p3 != null)
            {
                encoder.EncodeTagged(
                    1,
                    TagFormat.F4,
                    size: 4,
                    p3.Value,
                    (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
            }

            if (p4 != null)
            {
                encoder.EncodeTagged(
                    2,
                    TagFormat.OVSize,
                    p4,
                    (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
            }
            // payload: (int 4 bytes) + (string 1 byte size + 12 bytes contents) +
            // (tagged int 0 | tag 1 byte, size 1 byte, int 4 bytes) +
            // (tagged string 0 |  tag 1 byte, size 4 byte, string 13 bytes)
            int size = 4 + 13 + (p3 == null ? 0 : 6) + (p4 == null ? 0 : 18);
            SliceEncoder.EncodeVarULong((ulong)size, sizePlaceHolder);
            return PipeReader.Create(new ReadOnlySequence<byte>(bufferWriter.WrittenMemory));
        }
    }
}
