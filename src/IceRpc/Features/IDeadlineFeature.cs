// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Represents the expiration time of a request. Once the deadline of a two-way request is reached, the caller
/// is no longer interested in the response and the request should be discarded by the application code and IceRPC. For
/// a one-way request, the processing of the request should be canceled once its deadline is reached. The value of this
/// deadline feature is transmitted with requests using the <see cref="RequestFieldKey.Deadline" /> field.</summary>
public interface IDeadlineFeature
{
    /// <summary>Gets the value of deadline.</summary>
    /// <value>The deadline value. The <see cref="DateTime.MaxValue" /> means no deadline.</value>
    DateTime Value { get; }
}
