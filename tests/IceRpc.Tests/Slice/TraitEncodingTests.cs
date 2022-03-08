// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class TraitEncodingTests
{
    private IActivator _activator;
    private Memory<byte> _buffer;
    private MemoryBufferWriter _bufferWriter;

    /// <summary>Common setup for the trait decoding tests.</summary>
    public TraitEncodingTests()
    {
        _buffer = new byte[1024];
        _bufferWriter = new MemoryBufferWriter(_buffer);
        _activator = ActivatorFactory.Instance.Get(typeof(TraitEncodingTests).Assembly);
    }

    /// <summary>Verifies the struct's EncodeTrait generated method correctly encodes a struct as a trait.</summary>
    [Test]
    public void Encoding_a_struct_as_a_trait()
    {
        var encoder = new SliceEncoder(_bufferWriter, Encoding.Slice20);
        var decoder = new SliceDecoder(_buffer, Encoding.Slice20, activator: _activator);
        var traitStructA = new TraitStructA("Foo");

        traitStructA.EncodeTrait(ref encoder);

        Assert.That(decoder.DecodeString(), Is.EqualTo("::IceRpc::Slice::Tests::TraitStructA"));
        Assert.That(new TraitStructA(ref decoder), Is.EqualTo(traitStructA));
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}"/> method correctly decodes a trait into a concrete
    /// type.</summary>
    [Test]
    public void Decoding_a_trait_as_a_struct()
    {
        var encoder = new SliceEncoder(_bufferWriter, Encoding.Slice20);
        encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructB");
        var traitStructB1 = new TraitStructB(79);
        traitStructB1.Encode(ref encoder);
        var decoder = new SliceDecoder(_buffer, Encoding.Slice20, activator: _activator);

        var traitStructB2 = decoder.DecodeTrait<TraitStructB>();

        Assert.That(traitStructB2, Is.EqualTo(traitStructB1));
    }

    /// <summary>Verify that <see cref="SliceDecoder.DecodeTrait{T}"/> method correctly decodes a trait as an
    /// interface.</summary>
    [Test]
    public void Decoding_a_trait_as_an_interface()
    {
        var encoder = new SliceEncoder(_bufferWriter, Encoding.Slice20);
        var decoder = new SliceDecoder(_buffer, Encoding.Slice20, activator: _activator);
        var tsa = new TraitStructA("Bar");
        encoder.EncodeString("::IceRpc::Slice::Tests::TraitStructA");
        tsa.Encode(ref encoder);

        IMyTraitA decodedTrait = decoder.DecodeTrait<IMyTraitA>();

        Assert.That(decodedTrait.GetString(), Is.EqualTo("Bar"));
    }

    /// <summary>Verifies that decoding a trait with a mismatched type fails with <see cref="InvalidDataException"/>.
    /// </summary>
    [Test]
    public void Decoding_a_mismatched_type_fails()
    {
        // Arrange
        var encoder = new SliceEncoder(_bufferWriter, Encoding.Slice20);
        var traitStructB = new TraitStructB(97);
        traitStructB.EncodeTrait(ref encoder);

        // Act/Assert
        Assert.Throws<InvalidDataException>(() =>
            new SliceDecoder(_buffer, Encoding.Slice20, activator: _activator).DecodeTrait<IMyTraitA>());
    }

    /// <summary>Verifies that decoding a trait with an unknown type ID fails with <see cref="InvalidDataException"/>.
    /// </summary>
    [Test]
    public void Decoding_an_unknown_type_id_fails()
    {
        var encoder = new SliceEncoder(_bufferWriter, Encoding.Slice20);

        var traitStructB = new TraitStructB(42);
        encoder.EncodeString("::IceRpc::Slice::Tests::FakeTrait");
        traitStructB.Encode(ref encoder);

        Assert.Throws<InvalidDataException>(() =>
            new SliceDecoder(_buffer, Encoding.Slice20, activator: _activator).DecodeTrait<IMyTraitB>());
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
