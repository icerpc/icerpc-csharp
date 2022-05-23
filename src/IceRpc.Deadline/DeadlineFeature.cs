// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>The default implementation of <see cref="IDeadlineFeature"/>.</summary>
public sealed class DeadlineFeature : IDeadlineFeature
{
    /// <inheritdoc/>
    public DateTime Value { get; init; } = DateTime.MaxValue;
}
