// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Timeout;

/// <summary>A feature to customize the invocation timeout.</summary>
public interface ITimeoutFeature
{
    /// <summary>Gets the timeout for the invocation.</summary>
    TimeSpan Value { get; }
}
