// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Deadline;

/// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the caller is no
/// longer interested in the response and discards the request. This deadline feature is encoded into the deadline field
/// by the deadline interceptor and decoded from a field by the deadline middleware.</summary>
public interface IDeadlineFeature
{
    /// <summary>Gets the value of deadline. <see cref="DateTime.MaxValue"/> means no deadline.</summary>
    DateTime Value { get; }
}
