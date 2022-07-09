// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>Creates a new service proxy. This delegate is used by <see cref="ISliceFeature"/> and
/// <see cref="SliceDecoder"/>.</summary>
/// <param name="serviceAddress">The service address of the new proxy. It can be a relative service address with a null
/// protocol.</param>
/// <param name="proxyInvoker">When decoding an incoming response, the invoker of the proxy that sent the request. Null
/// when decoding an incoming requests.</param>
/// <param name="connectionContext">The connection context of the incoming request or response being decoded.</param>
/// <param name="encodeOptions">The Slice encode options from the request, or null if this feature is not set.</param>
public delegate ServiceProxy ServiceProxyFactory(
    ServiceAddress serviceAddress,
    IInvoker? proxyInvoker,
    IConnectionContext? connectionContext,
    SliceEncodeOptions? encodeOptions);
