// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

public sealed class TraitEncodingTests
{
    /// <summary>Verifies that decoding a trait with a mismatched type fails with <see cref="InvalidDataException" />.
    /// </summary>
    [Test]
    public void Decoding_a_mismatched_type_fails()
    {
        // Arrange
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), SliceEncoding.Slice2);
        var traitStructB = new TraitStructB(97);
        traitStructB.EncodeTrait(ref encoder);

        // Act/Assert
        Assert.That(
            () => new SliceDecoder(
                buffer,
                SliceEncoding.Slice2,
                activator: SliceDecoder.GetActivator(typeof(TraitStructB).Assembly))
                .DecodeTrait<IMyTraitA>(),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}" /> method correctly decodes a trait into a concrete
    /// type.</summary>
    [Test]
    public void Decoding_a_trait_as_a_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString("::IceRpc::Tests::Slice::TraitStructB");
        var traitStructB1 = new TraitStructB(79);
        traitStructB1.Encode(ref encoder);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(TraitStructB).Assembly));

        TraitStructB traitStructB2 = decoder.DecodeTrait<TraitStructB>();

        Assert.That(traitStructB2, Is.EqualTo(traitStructB1));
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}" /> method correctly decodes a trait as an
    /// interface.</summary>
    [Test]
    public void Decoding_a_trait_as_an_interface()
    {
        var buffer = new MemoryBufferWriter(new byte[1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString("::IceRpc::Tests::Slice::TraitStructA");
        var tsa = new TraitStructA("Bar");
        tsa.Encode(ref encoder);
        var decoder = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    activator: SliceDecoder.GetActivator(typeof(TraitStructA).Assembly));

        IMyTraitA decodedTrait = decoder.DecodeTrait<IMyTraitA>();

        Assert.That(decodedTrait.GetString(), Is.EqualTo("Bar"));
    }

    /// <summary>Verifies that decoding a trait with an unknown type ID fails with <see cref="InvalidDataException" />.
    /// </summary>
    [Test]
    public void Decoding_an_unknown_type_id_fails()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var traitStructB = new TraitStructB(42);
        encoder.EncodeString("::IceRpc::Tests::Slice::FakeTrait");
        traitStructB.Encode(ref encoder);

        // Act/Assert
        Assert.That(
            () => new SliceDecoder(
                buffer.WrittenMemory,
                SliceEncoding.Slice2,
                activator: SliceDecoder.GetActivator(typeof(TraitStructB).Assembly))
                .DecodeTrait<IMyTraitB>(),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that nested trait decoding fails with <see cref="InvalidDataException" /> after reaching
    /// the decoder max depth.</summary>
    /// <param name="depth">The decoder max depth.</param>
    [TestCase(100)]
    [TestCase(500)]
    public void Nested_trait_decoding_fails_after_reaching_decoder_max_depth(int depth)
    {
        Assert.That(
            () =>
            {
                var buffer = new MemoryBufferWriter(new byte[depth * 64]);
                var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
                for (int i = 0; i < depth; i++)
                {
                    encoder.EncodeString(NestedTraitStruct.SliceTypeId);
                }
                var decoder = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    maxDepth: depth - 1,
                    activator: SliceDecoder.GetActivator(typeof(NestedTraitStruct).Assembly));

                decoder.DecodeTrait<NestedTraitStruct>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies the struct's EncodeTrait generated method correctly encodes a struct as a trait.</summary>
    [Test]
    public void Encoding_a_struct_as_a_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var traitStructA = new TraitStructA("Foo");

        traitStructA.EncodeTrait(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(TraitStructA).Assembly));
        Assert.That(decoder.DecodeString(), Is.EqualTo("::IceRpc::Tests::Slice::TraitStructA"));
        Assert.That(new TraitStructA(ref decoder), Is.EqualTo(traitStructA));
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

public partial interface ITryNested { }
public partial record struct NestedTraitStruct : ITryNested { }
