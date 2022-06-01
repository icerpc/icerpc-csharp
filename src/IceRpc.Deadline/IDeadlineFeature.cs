// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the caller is no
/// longer interested in the response. This deadline feature is encoded into the deadline field by the deadline
/// interceptor and decoded from a field by the deadline middleware. When a request carries a deadline feature the
/// caller should also pass a cancelable cancellation token to the invocation, the
/// <see cref="DeadlineInterceptor(IInvoker, TimeSpan)"/> default timeout or the timeout specified with the
/// <see cref="ITimeoutFeature"/> will be ignored.</summary>
public interface IDeadlineFeature
{
    /// <summary>Gets the value of deadline. <see cref="DateTime.MaxValue"/> means no deadline.</summary>
    DateTime Value { get; }
}
