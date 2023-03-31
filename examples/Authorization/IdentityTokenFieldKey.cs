// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace AuthorizationExample;

/// <summary>The shared <see cref="RequestFieldKey" /> used to carry the identity token in a request's field.</summary>
public static class IdentityTokenFieldKey
{
    public const RequestFieldKey Value = (RequestFieldKey)100;
}
