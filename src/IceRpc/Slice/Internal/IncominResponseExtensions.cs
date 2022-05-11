// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
namespace IceRpc.Slice.Internal;

/// <summary>Extension methods for <see cref="IncomingResponse"/>.</summary>
internal static class IncomingResponseExtensions
{
    internal static SliceDecodePayloadOptions GetDecodePayloadOptions(
        this IncomingResponse response,
        OutgoingRequest request) =>
        request.Features.Get<SliceDecodePayloadOptions>() ??
        response.Connection.Features.Get<SliceDecodePayloadOptions>() ??
        SliceDecodePayloadOptions.Default;
}
