// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ClassTests
{
    [Test]
    public void Encode_decode_class()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeClass(new MyClassA
        {
            TheB = new MyClassB(),
            TheC = new MyClassC(),
        });
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyClassA).Assembly));

        var value = decoder.DecodeClass<MyClassA>();

        Assert.That(value, Is.Not.Null);
        Assert.That(value.TheB, Is.Not.Null);
        Assert.That(value.TheC, Is.Not.Null);
    }

    [Test]
    public void Encode_decode_class_with_null_references()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeClass(new MyClassA());
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyClassA).Assembly));

        var value = decoder.DecodeClass<MyClassA>();

        Assert.That(value, Is.Not.Null);
        Assert.That(value.TheB, Is.Null);
        Assert.That(value.TheC, Is.Null);
    }

    [Test]
    public void Encode_decode_class_graph()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var theA = new MyClassA();
        var theB = new MyClassB();
        var theC = new MyClassC();

        theA.TheB = theB;
        theA.TheC = theC;

        theB.TheC = theC;

        theC.TheB = theB;

        encoder.EncodeClass(theA);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyClassA).Assembly));

        var value = decoder.DecodeClass<MyClassA>();

        Assert.That(value, Is.Not.Null);
        Assert.That(value.TheB, Is.EqualTo(value.TheC!.TheB));
        Assert.That(value.TheC, Is.EqualTo(value.TheB!.TheC));
    }

    [Test]
    public void Encode_decode_class_with_compact_id()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeClass(new MyDerivedCompactClass());
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyClassA).Assembly));

        var value = decoder.DecodeClass<MyDerivedCompactClass>();

        Assert.That(value, Is.Not.Null);
    }

    [Test]
    public void Class_graph_max_depth()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var theA = new MyClassA();
        var theB = new MyClassB();
        var theC = new MyClassC();

        theA.TheB = theB;
        theA.TheC = theC;
        for (int i = 0; i < 100; i++)
        {
            theC = new MyClassC();
            theC.TheB = new MyClassB();
            theB!.TheC = theC;
            theB = theB.TheC.TheB;
        }

        encoder.EncodeClass(theA);

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(
                buffer.WrittenMemory,
                SliceEncoding.Slice1,
                activator: SliceDecoder.GetActivator(typeof(MyClassA).Assembly),
                maxDepth: 100);
            decoder.DecodeClass<MyClassA>();
        },
        Throws.TypeOf<InvalidDataException>());
    }
}
