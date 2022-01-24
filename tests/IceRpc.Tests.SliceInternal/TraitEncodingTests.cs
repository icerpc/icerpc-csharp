// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.SliceInternal
{
    public sealed class TraitEncodingTests
    {
        [Test]
        public void Trait_EncodingAsync()
        {
            // TODO move this into a TypeId Generation unit test.
            // Test the generation of type-ids on structs.
            Assert.That(
                typeof(TraitStructA).GetIceTypeId()!,
                Is.EqualTo("::IceRpc::Tests::SliceInternal::TraitStructA")
            );

            Memory<byte> buffer = new byte[1024];
            var encoding = Slice20Encoding.Instance;
            var activator = ActivatorFactory.Instance.Get(typeof(TraitEncodingTests).Assembly);

            // Test encoding traits with the generated code.
            {
                var bufferWriter = new SingleBufferWriter(buffer);
                var encoder = new IceEncoder(bufferWriter, encoding);
                var decoder = new IceDecoder(buffer, encoding, activator: activator);

                var tsa = new TraitStructA("Foo");
                tsa.EncodeTrait(ref encoder);

                Assert.That(
                    decoder.DecodeString(),
                    Is.EqualTo("::IceRpc::Tests::SliceInternal::TraitStructA")
                );
                Assert.That(
                    new TraitStructA(ref decoder),
                    Is.EqualTo(tsa)
                );
            }

            // Test decoding a trait to a concrete type.
            {
                var bufferWriter = new SingleBufferWriter(buffer);
                var encoder = new IceEncoder(bufferWriter, encoding);
                var decoder = new IceDecoder(buffer, encoding, activator: activator);

                var tsb = new TraitStructB(79);
                encoder.EncodeString("::IceRpc::Tests::SliceInternal::TraitStructB");
                tsb.Encode(ref encoder);

                Assert.That(decoder.DecodeTrait<TraitStructB>(), Is.EqualTo(tsb));
            }

            // Test decoding a trait to an interface type.
            {
                var bufferWriter = new SingleBufferWriter(buffer);
                var encoder = new IceEncoder(bufferWriter, encoding);
                var decoder = new IceDecoder(buffer, encoding, activator: activator);

                var tsa = new TraitStructA("Bar");
                encoder.EncodeString("::IceRpc::Tests::SliceInternal::TraitStructA");
                tsa.Encode(ref encoder);

                ITraitA decodedTrait = decoder.DecodeTrait<ITraitA>();
                Assert.That(decodedTrait.GetString(), Is.EqualTo("Bar"));
            }

            // Test that decoding a mismatched type fails.
            {
                var bufferWriter = new SingleBufferWriter(buffer);
                var encoder = new IceEncoder(bufferWriter, encoding);
                var decoder = new IceDecoder(buffer, encoding, activator: activator);

                var tsb = new TraitStructB(97);
                tsb.EncodeTrait(ref encoder);

                try
                {
                    decoder.DecodeTrait<ITraitA>();
                    Assert.Fail();
                }
                catch (InvalidDataException e)
                {
                    Assert.That(e.Message, Is.EqualTo("Decoded struct of type 'IceRpc.Tests.SliceInternal.TraitStructB' does not implement expected trait 'IceRpc.Tests.SliceInternal.ITraitA'"));
                }
            }

            // Test that decoding an unknown type-id fails.
            {
                var bufferWriter = new SingleBufferWriter(buffer);
                var encoder = new IceEncoder(bufferWriter, encoding);
                var decoder = new IceDecoder(buffer, encoding, activator: activator);

                var tsb = new TraitStructB(42);
                encoder.EncodeString("::IceRpc::Tests::SliceInternal::FakeTrait");
                tsb.Encode(ref encoder);

                try
                {
                    decoder.DecodeTrait<ITraitB>();
                    Assert.Fail();
                }
                catch (InvalidDataException e)
                {
                    Assert.That(e.Message, Is.EqualTo("Failed to decode struct of type 'IceRpc.Tests.SliceInternal.ITraitB' from type id '::IceRpc::Tests::SliceInternal::FakeTrait'"));
                }
            }
        }
    }

    public partial interface ITraitA
    {
        string GetString();
    }

    public partial interface ITraitB
    {
        long GetLong();
    }

    public partial record struct TraitStructA : ITraitA
    {
        public string GetString()
        {
            return this.S;
        }
    }

    public partial record struct TraitStructB : ITraitB
    {
        public long GetLong()
        {
            return this.L;
        }
    }
}
