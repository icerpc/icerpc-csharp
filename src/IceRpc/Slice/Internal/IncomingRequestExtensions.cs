// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
namespace IceRpc.Slice.Internal;

/// <summary>Extension methods for <see cref="IncomingRequest"/>.</summary>
internal static class IncomingRequestExtensions
{
    internal static SliceDecodePayloadOptions GetDecodePayloadOptions(this IncomingRequest request) =>
        request.Features.Get<SliceDecodePayloadOptions>() ??
        request.Connection.Features.Get<SliceDecodePayloadOptions>() ??
        SliceDecodePayloadOptions.Default;
}
