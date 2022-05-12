// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
namespace IceRpc.Slice.Internal;

/// <summary>Extension methods for <see cref="IncomingResponse"/>.</summary>
internal static class IncomingResponseExtensions
{
    internal static SliceDecodeOptions GetSliceDecodeOptions(
        this IncomingResponse response,
        OutgoingRequest request) =>
        request.Features.Get<SliceDecodeOptions>() ??
        response.Connection.Features.Get<SliceDecodeOptions>() ??
        SliceDecodeOptions.Default;
}
