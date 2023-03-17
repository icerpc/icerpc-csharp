// Copyright (c) ZeroC, Inc.

namespace IceRpc.Deadline;

/// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the caller is no
/// longer interested in the response and the request should be discarded by the application code and IceRPC. This
/// deadline feature is encoded into the deadline field by the deadline interceptor and decoded from a field by the
/// deadline middleware.</summary>
public interface IDeadlineFeature
{
    /// <summary>Gets the value of deadline.</summary>
    /// <value>The deadline value. The <see cref="DateTime.MaxValue" /> means no deadline.</value>
    DateTime Value { get; }
}
