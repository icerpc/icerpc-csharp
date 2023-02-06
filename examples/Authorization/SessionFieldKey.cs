// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace AuthorizationExample;

/// <summary>The shared <see cref="RequestFieldKey" /> used by the client and server to carry the session
/// token.</summary>
public static class SessionFieldKey
{
    public const RequestFieldKey Value = (RequestFieldKey)100;
}
