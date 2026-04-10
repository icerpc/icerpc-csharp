// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.Base.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class SequenceMappingTests
{
    [Test]
    public void Encode_decode_seq_struct()
    {
        // Arrange
        var original = CreateSeqStruct();

        // Act
        SeqStruct decoded = EncodeDecode(original);

        // Assert
        // We can't use Assert.That(decoded, Is.EqualTo(original)) because record struct equality uses
        // EqualityComparer<T>.Default for each field, and for collection fields this means reference equality.
        Assert.That(decoded.StringSeq, Is.EqualTo(original.StringSeq));
        Assert.That(decoded.StringSeqSeq, Is.EqualTo(original.StringSeqSeq));
        Assert.That(decoded.StringList, Is.EqualTo(original.StringList));
        Assert.That(decoded.StringListList, Is.EqualTo(original.StringListList));
        Assert.That(decoded.StringQueue, Is.EqualTo(original.StringQueue));
        Assert.That(decoded.StringQueueQueue, Is.EqualTo(original.StringQueueQueue));
        Assert.That(decoded.StringLinkedList, Is.EqualTo(original.StringLinkedList));
        Assert.That(decoded.StringLinkedListLinkedList, Is.EqualTo(original.StringLinkedListLinkedList));
        Assert.That(decoded.StringStack, Is.EqualTo(original.StringStack));
        Assert.That(decoded.StringStackStack, Is.EqualTo(original.StringStackStack));
        Assert.That(decoded.CustomStringSeq, Is.EqualTo(original.CustomStringSeq));
        Assert.That(decoded.CustomStringSeqSeq, Is.EqualTo(original.CustomStringSeqSeq));
    }

    private static SeqStruct EncodeDecode(SeqStruct original)
    {
        var buffer = new MemoryBufferWriter(new byte[4096]);
        var encoder = new IceEncoder(buffer);
        original.Encode(ref encoder);
        var decoder = new IceDecoder(buffer.WrittenMemory);
        return new SeqStruct(ref decoder);
    }

    private static SeqStruct CreateSeqStruct() =>
        new(
            stringSeq: new string[] { "hello", "world" },
            stringSeqSeq: new IList<string>[]
            {
                ["a", "b"],
                ["c"],
            },
            stringList: new List<string> { "foo", "bar" },
            stringListList: new List<IList<string>>
            {
                new List<string> { "x", "y" },
                new List<string> { "z" },
            },
            stringQueue: new Queue<string>(["alpha", "beta"]),
            stringQueueQueue: new Queue<Queue<string>>(
            [
                new Queue<string>(["one", "two"]),
                new Queue<string>(["three"]),
            ]),
            stringLinkedList: new LinkedList<string>(["first", "second"]),
            stringLinkedListLinkedList: new LinkedList<LinkedList<string>>(
            [
                new LinkedList<string>(["p", "q"]),
                new LinkedList<string>(["r"]),
            ]),
            stringStack: new Stack<string>(["top", "bottom"]),
            stringStackStack: new Stack<Stack<string>>(
            [
                new Stack<string>(["s1a", "s1b"]),
                new Stack<string>(["s2a"]),
            ]),
            customStringSeq: CustomSequence<string>.Create(["custom1", "custom2"]),
            customStringSeqSeq: CustomSequence<CustomSequence<string>>.Create(
            [
                CustomSequence<string>.Create(["ca", "cb"]),
                CustomSequence<string>.Create(["cc"]),
            ]));
}
