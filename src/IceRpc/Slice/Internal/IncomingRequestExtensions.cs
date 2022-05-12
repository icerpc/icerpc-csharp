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

    // TODO: the return type of these 2 methods is inconsistent.
    internal static SliceEncodeOptions? GetSliceEncodeOptions(this IncomingRequest request) =>
        request.Features.Get<SliceEncodeOptions>() ?? request.Connection.Features.Get<SliceEncodeOptions>();
}
