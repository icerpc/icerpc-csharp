// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(ParallelScope.All)]
public sealed class ExceptionTests
{
    public static IEnumerable<TestCaseData> Slice1OperationThrowsSource
    {
        get
        {
            yield return new TestCaseData(new InvalidDataException("invalid data"), StatusCode.InvalidData);
            yield return new TestCaseData(new MyExceptionWithOptionalMembers(), StatusCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), StatusCode.UnhandledException);
        }
    }

    public static IEnumerable<TestCaseData> Slice2OperationThrowsSource
    {
        get
        {
            yield return new TestCaseData(new InvalidDataException("invalid data"), StatusCode.InvalidData);
            yield return new TestCaseData(new MyDerivedException(), StatusCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), StatusCode.UnhandledException);
        }
    }

    [Test]
    public void Decode_slice1_derived_exception()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        encoder.StartSlice(MyDerivedException.SliceTypeId);
        encoder.EncodeInt32(30);
        encoder.EncodeInt32(40);
        encoder.EndSlice(lastSlice: false);

        encoder.StartSlice(MyException.SliceTypeId);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (taggedValue is not null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skipped
            encoder.EncodeTagged(
                10,
                TagFormat.F4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k is not null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        if (l is not null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (taggedValue is not null)
        {
            // Ensure that a tagged value not declared in the Slice definition is correctly skipped
            encoder.EncodeTagged(
                10,
                size: 4,
                taggedValue.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        bitSequenceWriter.Write(k is not null);
        if (k is not null)
        {
            encoder.EncodeInt32(k.Value);
        }
        bitSequenceWriter.Write(l is not null);
        if (l is not null)
        {
            encoder.EncodeInt32(l.Value);
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k is not null)
        {
            encoder.EncodeTagged(
                1,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        if (l is not null)
        {
            encoder.EncodeTagged(
                255,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
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
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
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
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
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
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.K));
        }
        if (bitSequenceReader.Read())
        {
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
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
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        if (k is not null)
        {
            Assert.That(
                decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeInt32(), useTagEndMarker: true),
                Is.EqualTo(value.K));
        }
        if (l is not null)
        {
            Assert.That(
                decoder.DecodeTagged(255, (ref SliceDecoder decoder) => decoder.DecodeInt32(), useTagEndMarker: true),
                Is.EqualTo(value.L));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
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
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(value.J));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(Slice2OperationThrowsSource))]
    public async Task Slice2_operation_throws_slice1_exception_fails(Exception throwException, StatusCode statusCode)
    {
        var coloc = new ColocTransport();
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions()
                {
                    Dispatcher = new Slice2ExceptionOperations(throwException),
                },
                ServerAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}/"))
            },
            multiplexedServerTransport: new SlicServerTransport(coloc.ServerTransport));
        server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress,
            },
            multiplexedClientTransport: new SlicClientTransport(coloc.ClientTransport));
        var proxy = new Slice2ExceptionOperationsProxy(connection);

        DispatchException? exception = Assert.CatchAsync<DispatchException>(() => proxy.OpThrowsAsync());

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(statusCode));
    }

    [Test, TestCaseSource(nameof(Slice1OperationThrowsSource))]
    public async Task Slice1_operation_throws_slice1_exception_fails(
        Exception throwException,
        StatusCode statusCode)
    {
        var coloc = new ColocTransport();
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions
                {
                    Dispatcher = new Slice1ExceptionOperations(throwException),
                },
                ServerAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}/"))
            },
            multiplexedServerTransport: new SlicServerTransport(coloc.ServerTransport));
        server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress,
            },
            multiplexedClientTransport: new SlicClientTransport(coloc.ClientTransport));
        var proxy = new Slice1ExceptionOperationsProxy(connection);

        DispatchException? caughtException = Assert.CatchAsync<DispatchException>(() => proxy.OpThrowsAsync());

        Assert.That(caughtException, Is.Not.Null);
        Assert.That(caughtException!.StatusCode, Is.EqualTo(statusCode));
    }

    class Slice2ExceptionOperations : Service, ISlice2ExceptionOperations
    {
        private readonly Exception _exception;

        public Slice2ExceptionOperations(Exception exception) => _exception = exception;

        public ValueTask OpThrowsAsync(IFeatureCollection features, CancellationToken cancellationToken = default) =>
            throw _exception;
    }

    class Slice1ExceptionOperations : Service, ISlice1ExceptionOperations
    {
        private readonly Exception _exception;

        public Slice1ExceptionOperations(Exception exception) => _exception = exception;
        public ValueTask OpThrowsAsync(IFeatureCollection features, CancellationToken cancellationToken = default) =>
            throw _exception;
    }
}
