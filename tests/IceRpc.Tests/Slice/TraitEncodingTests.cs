// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public sealed class TraitEncodingTests
{
    [Test]
    public void Trait_Encoding()
    {
        // TODO move this into a TypeId Generation unit test.
        // Test the generation of type-ids on structs.
        Assert.That(
            typeof(TraitStructA).GetSliceTypeId()!,
            Is.EqualTo("::IceRpc::Slice::Tests::TraitStructA")
        );

        Memory<byte> buffer = new byte[1024];
        var encoding = Encoding.Slice20;
        var activator = ActivatorFactory.Instance.Get(typeof(TraitEncodingTests).Assembly);

        // Test encoding traits with the generated code.
        {
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, encoding);
            var decoder = new SliceDecoder(buffer, encoding, activator: activator);

            var tsa = new TraitStructA("Foo");
            tsa.EncodeTrait(ref encoder);

            Assert.That(decoder.DecodeString(), Is.EqualTo("::IceRpc::Slice::Tests::TraitStructA"));
            Assert.That(new TraitStructA(ref decoder), Is.EqualTo(tsa));
        }

        // Test decoding a trait to a concrete type.
        {
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, encoding);
            var decoder = new SliceDecoder(buffer, encoding, activator: activator);

            var tsb = new TraitStructB(79);
            encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructB");
            tsb.Encode(ref encoder);

            Assert.That(decoder.DecodeTrait<TraitStructB>(), Is.EqualTo(tsb));
        }

        // Test decoding a trait to an interface type.
        {
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, encoding);
            var decoder = new SliceDecoder(buffer, encoding, activator: activator);

            var tsa = new TraitStructA("Bar");
            encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructA");
            tsa.Encode(ref encoder);

            IMyTraitA decodedTrait = decoder.DecodeTrait<IMyTraitA>();
            Assert.That(decodedTrait.GetString(), Is.EqualTo("Bar"));
        }

        // Test that decoding a mismatched type fails.
        {
            var bufferWriter = new SingleBufferWriter(buffer);
           
            

            Assert.Throws<InvalidDataException>(() =>
            {
                var encoder = new SliceEncoder(bufferWriter, encoding);
                var decoder = new SliceDecoder(buffer, encoding, activator: activator);
                var tsb = new TraitStructB(97);
                tsb.EncodeTrait(ref encoder);
                decoder.DecodeTrait<IMyTraitA>();
            });
        }

        // Test that decoding an unknown type-id fails.
        {
            var bufferWriter = new SingleBufferWriter(buffer);
            

            Assert.Throws<InvalidDataException>(() =>
            {
                var encoder = new SliceEncoder(bufferWriter, encoding);
                var decoder = new SliceDecoder(buffer, encoding, activator: activator);

                var tsb = new TraitStructB(42);
                encoder.EncodeString("::IceRpc::Slice::Tests::FakeTrait");
                tsb.Encode(ref encoder);
                decoder.DecodeTrait<IMyTraitB>();
            });
        }
    }
}

public partial interface IMyTraitA
{
    string GetString();
}

public partial interface IMyTraitB
{
    long GetLong();
}

public partial record struct TraitStructA : IMyTraitA
{
    public string GetString() => S;
}

public partial record struct TraitStructB : IMyTraitB
{
    public long GetLong() => L;
}
