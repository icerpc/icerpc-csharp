// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Features.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DeadlineFeatureTests
{
    [Test]
    public void FromTimeout_rejects_non_positive_timeout()
    {
        Assert.That(() => DeadlineFeature.FromTimeout(TimeSpan.Zero), Throws.TypeOf<ArgumentException>());
        Assert.That(() => DeadlineFeature.FromTimeout(TimeSpan.FromSeconds(-1)), Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies FromTimeout rejects a timeout beyond CancelAfter's supported maximum instead of
    /// letting DateTime.UtcNow + timeout overflow with ArgumentOutOfRangeException.</summary>
    [Test]
    public void FromTimeout_rejects_timeout_beyond_cancel_after_max()
    {
        Assert.That(() => DeadlineFeature.FromTimeout(TimeSpan.MaxValue), Throws.TypeOf<ArgumentException>());
    }
}
