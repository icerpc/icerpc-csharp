// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Slice.Encoding.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ExceptionTests
{
    [Test]
    public void Decode_slice1_derived_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        encoder.StartSlice(MyDerivedException.SliceTypeId);
        encoder.EncodeInt(30);
        encoder.EncodeInt(40);
        encoder.EndSlice(lastSlice: false);

        encoder.StartSlice(MyException.SliceTypeId);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        encoder.EndSlice(lastSlice: true);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyException).Assembly));

        var value = decoder.DecodeUserException() as MyDerivedException;

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(30));
        Assert.That(value.L, Is.EqualTo(40));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice1_exception([Values(10, null)] int? taggedValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.StartSlice(MyException.SliceTypeId);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        if (taggedValue != null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skip
            encoder.EncodeTagged(
                10,
                TagFormat.F4,
                size: 4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        encoder.EndSlice(lastSlice: true);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory, 
            SliceEncoding.Slice1, 
            activator: SliceDecoder.GetActivator(typeof(MyException).Assembly));

        var value = decoder.DecodeUserException() as MyException;

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice1_exception_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.StartSlice(MyExceptionWithTaggedMembers.SliceTypeId);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        if (k != null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        if (l != null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        encoder.EndSlice(lastSlice: true);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyExceptionWithTaggedMembers).Assembly));

        var value = decoder.DecodeUserException() as MyExceptionWithTaggedMembers;

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception([Values(10, null)] int? taggedValue)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString("my custom exception");
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        if (taggedValue != null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skip
            encoder.EncodeTagged(
                10,
                TagFormat.F4,
                size: 4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyException(ref decoder);

        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString("my exception with tagged members");
        var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        bitSequenceWriter.Write(k != null);
        if (k != null)
        {
            encoder.EncodeInt(k.Value);
        }
        bitSequenceWriter.Write(l != null);
        if (l != null)
        {
            encoder.EncodeInt(l.Value);
        }
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyExceptionWithOptionalMembers(ref decoder);

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_exception_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString("my exception with tagged members");
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        if (k != null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        if (l != null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new MyExceptionWithTaggedMembers(ref decoder);

        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_slice2_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString(MyException.SliceTypeId);
        encoder.EncodeString("my exception");
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(MyException).Assembly));

        MyException value = decoder.DecodeTrait<MyException>();

        Assert.That(value, Is.Not.Null);
        Assert.That(value.Message, Is.EqualTo("my exception"));
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    // Encode

    [Test]
    public void Encode_slice1_derived_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyDerivedException(10, 20, 30, 40);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyException).Assembly));

        // TODO how we test this without using DecodeUserException?
        var decoded = decoder.DecodeUserException() as MyDerivedException;
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded.I, Is.EqualTo(expected.I));
        Assert.That(decoded.J, Is.EqualTo(expected.J));
        Assert.That(decoded.K, Is.EqualTo(expected.K));
        Assert.That(decoded.L, Is.EqualTo(expected.L));
    }

    [Test]
    public void Encode_slice1_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyException(10, 20);

        expected.Encode(ref encoder);

        // TODO how we test this without using DecodeUserException?
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyException).Assembly));
        var value = decoder.DecodeUserException() as MyException;
        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(expected.I));
        Assert.That(value.J, Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice1_exception_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyExceptionWithTaggedMembers(10, 20, k, l);

        expected.Encode(ref encoder);

        // TODO how we test this without using DecodeUserException?
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyExceptionWithTaggedMembers).Assembly));
        var value = decoder.DecodeUserException() as MyExceptionWithTaggedMembers;
        Assert.That(value, Is.Not.Null);
        Assert.That(value.I, Is.EqualTo(10));
        Assert.That(value.J, Is.EqualTo(20));
        Assert.That(value.K, Is.EqualTo(k));
        Assert.That(value.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyException("my exception", 10, 20);

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(value.Message));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.J));
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyExceptionWithOptionalMembers("my exception with optional members", 10, 20, k, l);

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(value.Message));
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.J));
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt(), Is.EqualTo(value.K));
        }
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt(), Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_exception_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyExceptionWithTaggedMembers("my exception with tagged members", 10, 20, k, l);

        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(value.Message));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.J));
        if (k != null)
        {
            Assert.That(
                decoder.DecodeTagged(1, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(value.K));
        }
        if (l != null)
        {
            Assert.That(
                decoder.DecodeTagged(255, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice2_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var value = new MyException("my exception", 10, 20);

        value.EncodeTrait(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyException.SliceTypeId));
        Assert.That(decoder.DecodeString(), Is.EqualTo(value.Message));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(value.J));
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public async Task Slice2_operation_throws_slice1_exception_fails()
    {
        var coloc = new ColocTransport();
        await using var server = new Server(new Configure.ServerOptions
        {
            Dispatcher = new Slice2ExceptionOperations(),
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            Endpoint = $"icerpc://{Guid.NewGuid()}/"
        });
        server.Listen();

        await using var connection = new Connection(new Configure.ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
        });
        var prx = Slice2ExceptionOperationsPrx.FromConnection(connection);

        DispatchException? exception = Assert.CatchAsync<DispatchException>(() => prx.OpThrowsAsync());

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ErrorCode, Is.EqualTo(DispatchErrorCode.UnhandledException));
    }

    [Test]
    public async Task Slice1_operation_throws_slice1_exception_fails()
    {
        var coloc = new ColocTransport();
        await using var server = new Server(new Configure.ServerOptions
        {
            Dispatcher = new Slice1ExceptionOperations(),
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            Endpoint = $"icerpc://{Guid.NewGuid()}/"
        });
        server.Listen();

        await using var connection = new Connection(new Configure.ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
        });
        var prx = Slice1ExceptionOperationsPrx.FromConnection(connection);

        DispatchException? exception = Assert.CatchAsync<DispatchException>(() => prx.OpThrowsAsync());

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ErrorCode, Is.EqualTo(DispatchErrorCode.UnhandledException));
    }

    class Slice2ExceptionOperations : Service, ISlice2ExceptionOperations
    {
        public ValueTask OpThrowsAsync(Dispatch dispatch, CancellationToken cancel = default) =>
            throw new MyDerivedException();
    }

    class Slice1ExceptionOperations : Service, ISlice1ExceptionOperations
    {
        public ValueTask OpThrowsAsync(Dispatch dispatch, CancellationToken cancel = default) =>
            throw new MyExceptionWithOptionalMembers();
    }
}
