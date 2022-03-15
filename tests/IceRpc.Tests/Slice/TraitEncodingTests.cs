// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public sealed class TraitEncodingTests
{
    /// <summary>Verifies that decoding a trait with a mismatched type fails with <see cref="InvalidDataException"/>.
    /// </summary>
    [Test]
    public void Decoding_a_mismatched_type_fails()
    {
        // Arrange
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), Encoding.Slice20);
        var traitStructB = new TraitStructB(97);
        traitStructB.EncodeTrait(ref encoder);

        // Act/Assert
        Assert.That(
            () => new SliceDecoder(
                buffer,
                Encoding.Slice20,
                activator: CreateActivator())
                .DecodeTrait<IMyTraitA>(),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}"/> method correctly decodes a trait into a concrete
    /// type.</summary>
    [Test]
    public void Decoding_a_trait_as_a_struct()
    {
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), Encoding.Slice20);
        encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructB");
        var traitStructB1 = new TraitStructB(79);
        traitStructB1.Encode(ref encoder);
        var decoder = new SliceDecoder(buffer, Encoding.Slice20, activator: CreateActivator());

        TraitStructB traitStructB2 = decoder.DecodeTrait<TraitStructB>();

        Assert.That(traitStructB2, Is.EqualTo(traitStructB1));
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}"/> method correctly decodes a trait as an
    /// interface.</summary>
    [Test]
    public void Decoding_a_trait_as_an_interface()
    {
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), Encoding.Slice20);
        var decoder = new SliceDecoder(buffer, Encoding.Slice20, activator: CreateActivator());
        var tsa = new TraitStructA("Bar");
        encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructA");
        tsa.Encode(ref encoder);

        IMyTraitA decodedTrait = decoder.DecodeTrait<IMyTraitA>();

        Assert.That(decodedTrait.GetString(), Is.EqualTo("Bar"));
    }

    /// <summary>Verifies that decoding a trait with an unknown type ID fails with <see cref="InvalidDataException"/>.
    /// </summary>
    [Test]
    public void Decoding_an_unknown_type_id_fails()
    {
        // Arrange
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), Encoding.Slice20);
        var traitStructB = new TraitStructB(42);
        encoder.EncodeString("::IceRpc::Slice::Tests::FakeTrait");
        traitStructB.Encode(ref encoder);

        // Act/Assert
        Assert.That(
            () => new SliceDecoder(
                buffer,
                Encoding.Slice20,
                activator: CreateActivator())
                .DecodeTrait<IMyTraitB>(),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies the struct's EncodeTrait generated method correctly encodes a struct as a trait.</summary>
    [Test]
    public void Encoding_a_struct_as_a_trait()
    {
        Memory<byte> buffer = new byte[1024];
        var encoder = new SliceEncoder(new MemoryBufferWriter(buffer), Encoding.Slice20);
        var decoder = new SliceDecoder(buffer, Encoding.Slice20, activator: CreateActivator());
        var traitStructA = new TraitStructA("Foo");

        traitStructA.EncodeTrait(ref encoder);

        Assert.That(decoder.DecodeString(), Is.EqualTo("::IceRpc::Slice::Tests::TraitStructA"));
        Assert.That(new TraitStructA(ref decoder), Is.EqualTo(traitStructA));
    }

    /// <summary>Helper method used to create the activator used with the test.</summary>
    private static IActivator CreateActivator() =>
        ActivatorFactory.Instance.Get(typeof(TraitEncodingTests).Assembly);
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
