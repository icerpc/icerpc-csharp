// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>A feature to customize the invocation timeout, the timeout feature can be used to overwrite the default
///  timeout set with the <see cref="DeadlineInterceptor(IInvoker, TimeSpan)"/>.</summary>
public interface ITimeoutFeature
{
    /// <summary>Gets the timeout for the invocation.</summary>
    TimeSpan Value { get; }
}
