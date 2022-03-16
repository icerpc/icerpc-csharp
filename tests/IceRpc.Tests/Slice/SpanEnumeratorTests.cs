
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
    private static IEnumerable<TestCaseData> EnumeratorCurrentUpdatesSuccessfullySource
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
            foreach ((
                byte[] firstBytes,
                byte[] secondBytes,
                IList<Memory<byte>>? additionalMemory,
                int moves,
                int length,
                byte expected) in testData)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, moves, length, expected);
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Span_enumerator_fails_to_move_to_additional_spans_and_memory_if_none_provided(...)"/> test.
    /// </summary>
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
            foreach ((
                byte[] firstBytes,
                byte[] secondBytes,
                IList<Memory<byte>>? additionalMemory,
                int moves) in enumeratorTestArgsAndExpected)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, moves);
            }
        }
    }

    /// <summary>Tests that the SpanEnumerator will successfully enumerate to the second span and additional memory if
    /// it is provided as an argument. This test accomplishes this by checking if the operation `MoveNext` was
    /// successful and that the SpanEnumerator updated the state of `Current` correctly.</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    /// <param name="moves">The number of times to call `MoveNext`.</param>
    /// <param name="expectedLength">The expected number of bytes in `Current`</param>
    /// <param name="expectedValue">The expected value of the first byte in `Current`.</param>
    [Test, TestCaseSource(nameof(enumeratorCurrentUpdatesSuccessfullySource))]
    public void Span_enumerator_successfully_enumerates_to_additional_spans_and_memory(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int moves,
        int expectedLength,
        byte expectedValue)
    {
        // Arrange
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0; i < moves - 1; i++) enumerator.MoveNext();

        // Act
        bool opperationSuccessful = enumerator.MoveNext();

        // Assert
        Assert.That(opperationSuccessful, Is.True);
        Assert.That(enumerator.Current.Length, Is.EqualTo(expectedLength));
        Assert.That(enumerator.Current[0], Is.EqualTo(expectedValue));
    }

    /// <summary>Tests that the SpanEnumerator will not enumerate past the last provided span or additional memory if
    /// This test accomplishes this by checking if the operation `MoveNext` returns false when there is no
    /// subsequent provided.</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    /// <param name="moves">The number of times to call `MoveNext`.</param>
    [Test, TestCaseSource(nameof(enumeratorNextFailsSource))]
    public void Span_enumerator_fails_to_move_to_additional_spans_and_memory_if_none_provided(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int moves)
    {
        // Arrange
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0; i < moves; i++) enumerator.MoveNext();

        // Act
        bool opperationSuccessful = enumerator.MoveNext();

        // Assert
        Assert.That(opperationSuccessful, Is.False);
    }
}
