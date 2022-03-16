
// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SpanEnumeratorTests
{
    /// <summary>Provides test case data for
    /// <see cref="Span_enumerator_successfully_enumerates_to_additional_spans_and_memory(...)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> enumeratorCurrentUpdatesSuccessfullySource
    {
        get
        {
            (byte[], byte[], IList<Memory<byte>>?, int, int, byte)[] testData =
            {
                (
                    new byte[] { 1, 2, 3 },
                    Array.Empty<byte>(),
                    null,
                    1,
                    3,
                    1
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    null,
                    2,
                    3,
                    4
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 }
                    },
                    3,
                    3,
                    7
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 },
                        new byte[] { 10, 11, 12 }
                    },
                    4,
                    3,
                    10
                ),
            };
            foreach ((byte[] firstBytes, byte[] secondBytes, IList<Memory<byte>>? additionalMemory, int moves, int length, byte expected) in testData)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, moves, length, expected);
            }
        }
    }

    /// <summary> TODO </summary>
    private static IEnumerable<TestCaseData> enumeratorNextFailsSource
    {
        get
        {
            (byte[], byte[], IList<Memory<byte>>?, int)[] enumeratorTestArgsAndExpected =
            {
                (
                    new byte[] { 1, 2, 3 },
                    Array.Empty<byte>(),
                    null,
                    1
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    null,
                    2
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 }
                    },
                    3
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 },
                        new byte[] { 10, 11, 12 }
                    },
                    4
                ),
            };
            foreach ((byte[] firstBytes, byte[] secondBytes, IList<Memory<byte>>? additionalMemory, int moves) in enumeratorTestArgsAndExpected)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, moves);
            }
        }
    }

    /// <summary> TODO </summary>
    [Test, TestCaseSource(nameof(enumeratorCurrentUpdatesSuccessfullySource))]
    public void Span_enumerator_successfully_enumerates_to_additional_spans_and_memory(byte[] firstBytes, byte[] secondBytes, IList<Memory<byte>>? additionalMemory, int moves, int length, byte expected)
    {
        // Arrange
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0 ; i < moves - 1; i++) enumerator.MoveNext();

        // Act
        bool opperationSuccessful = enumerator.MoveNext();

        // Assert
        Assert.That(opperationSuccessful, Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(length));
        Assert.That(enumerator.Current[0], Is.EqualTo(expected));
    }

    /// <summary>Provides test case data for
    /// <see cref="Span_enumerator_successfully_enumerates_to_additional_spans_and_memory(...)"/> test.
    /// </summary>
    [Test, TestCaseSource(nameof(enumeratorNextFailsSource))]
    public void Span_enumerator_fails_to_move_to_additional_spans_and_memory_if_none_provided(byte[] firstBytes, byte[] secondBytes, IList<Memory<byte>>? additionalMemory, int moves)
    {
        // Arrange
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0 ; i < moves; i++) enumerator.MoveNext();

        // Act
        bool opperationSuccessful = enumerator.MoveNext();

        // Assert
        Assert.That(opperationSuccessful, Is.False);
    }
}
