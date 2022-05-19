// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Timeout;

/// <summary>The default implementation for <see cref="ITimeoutFeature"/>.</summary>
public sealed class TimeoutFeature : ITimeoutFeature
{
    /// <inheritdoc/>
    public TimeSpan Timeout { get; init; }
}
