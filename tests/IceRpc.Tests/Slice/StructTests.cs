// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public partial record struct MyStruct : IMyTrait { }
public interface INotImplementedTrait : ITrait { }

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    public static IEnumerable<MyStruct> EncodeDecodeSimpleStructSource =>
        Enumerable.Range(0, 12).Select(x => new MyStruct(x, x * 33));

    public static IEnumerable<MyStructWithTaggedMembers> EncodeDecodeStructWithTaggedMembersSource =>
        Enumerable.Range(0, 12).Select(
            i => new MyStructWithTaggedMembers(
                i,
                i * 33,
                i % 2 == 0 ? i : null,
                i % 3 == 0 ? i : null));

    [Test, TestCaseSource(nameof(EncodeDecodeSimpleStructSource))]
    public void Encode_decode_simple_struct(MyStruct expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        expected.Encode(ref encoder);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStruct(ref decoder);

        Assert.That(decoded, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(EncodeDecodeStructWithTaggedMembersSource))]
    public void Encode_decode_struct_with_tagged_members(MyStructWithTaggedMembers expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        expected.Encode(ref encoder);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStructWithTaggedMembers(ref decoder);

        Assert.That(decoded, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(EncodeDecodeSimpleStructSource))]
    public void Encode_decode_simple_struct_as_trait(MyStruct expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        expected.EncodeTrait(ref encoder);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(MyStruct).Assembly));

        IMyTrait decoded = decoder.DecodeTrait<IMyTrait>();

        Assert.That(decoded, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_struct_as_not_implemented_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        new MyStruct(0, 0).EncodeTrait(ref encoder);

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    activator: SliceDecoder.GetActivator(typeof(MyStruct).Assembly));
                decoder.DecodeTrait<INotImplementedTrait>();
            },
            Throws.TypeOf<InvalidDataException>());
    }
}
