// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
namespace IceRpc.Slice.Internal;

/// <summary>Extension methods for <see cref="IncomingRequest"/>.</summary>
internal static class IncomingRequestExtensions
{
    internal static SliceDecodeOptions GetSliceDecodeOptions(this IncomingRequest request) =>
        request.Features.Get<SliceDecodeOptions>() ??
        request.Connection.Features.Get<SliceDecodeOptions>() ??
        SliceDecodeOptions.Default;
}
