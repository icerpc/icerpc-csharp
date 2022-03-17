
// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SpanEnumeratorTests
{
    /// <summary>Provides test case data for
    /// <see cref="Move_to_next_span(...)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> EnumeratorCurrentUpdatesSuccessfullySource
    {
        get
        {
            (byte[], byte[], IList<Memory<byte>>?, int, byte[])[] testData =
            {
                (
                    new byte[] { 1, 2, 3 },
                    Array.Empty<byte>(),
                    null,
                    1,
                    new byte[] { 1, 2, 3 }
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    null,
                    2,
                    new byte[] { 4, 5, 6 }
                ),
                (
                    new byte[] { 1, 2, 3 },
                    new byte[] { 4, 5, 6 },
                    new Memory<byte>[]
                    {
                        new byte[] { 7, 8, 9 }
                    },
                    3,
                    new byte[] { 7, 8, 9 }
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
                    new byte[] { 10, 11, 12 }
                ),
            };
            foreach ((
                byte[] firstBytes,
                byte[] secondBytes,
                IList<Memory<byte>>? additionalMemory,
                int moves,
                byte[] expected) in testData)
            {
                yield return new TestCaseData(firstBytes, secondBytes, additionalMemory, moves, expected);
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Move_pass_the_end_fails(...)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> EnumeratorNextFailsSource
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

    /// <summary>Verifies that calling <see cref="SpanEnumerator.MoveNext"/> correctly enumerates through the spans
    /// held by the enumerator. </summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    /// <param name="moves">The number of times to call `MoveNext`.</param>
    /// <param name="expected">The expected byte array in `Current`.</param>
    [Test, TestCaseSource(nameof(EnumeratorCurrentUpdatesSuccessfullySource))]
    public void Move_to_next_span(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int moves,
        byte[] expected)
    {
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0; i < moves - 1; i++) enumerator.MoveNext();

        bool opperationSuccessful = enumerator.MoveNext();

        Assert.That(opperationSuccessful, Is.True);
        Assert.That(enumerator.Current.ToArray(), Is.EqualTo(expected.ToArray()));
    }

    /// <summary>Verifies that calling <see cref="SpanEnumerator.MoveNext"/> will not enumerate past the final
    /// span or memory provided to the enumerator.</summary>
    /// <param name="firstBytes">The bytes that will be used to create the first span.</param>
    /// <param name="secondBytes">The bytes that will be used to create the second span. (Can be empty)</param>
    /// <param name="additionalMemory">The list of memory used for additional memory. (Optional)</param>
    /// <param name="moves">The number of times to call `MoveNext`.</param>
    [Test, TestCaseSource(nameof(EnumeratorNextFailsSource))]
    public void Move_pass_the_end_fails(
        byte[] firstBytes,
        byte[] secondBytes,
        IList<Memory<byte>>? additionalMemory,
        int moves)
    {
        var enumerator = new SpanEnumerator(firstBytes.AsSpan(), secondBytes.AsSpan(), additionalMemory);
        for (int i = 0; i < moves; i++) enumerator.MoveNext();

        bool opperationSuccessful = enumerator.MoveNext();

        Assert.That(opperationSuccessful, Is.False);
    }
}
