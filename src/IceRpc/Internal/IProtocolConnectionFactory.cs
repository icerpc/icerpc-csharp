// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    internal delegate IncomingRequest IncomingRequestFactory(
        FeatureCollection features,
        IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields,
        PipeReader? fieldsPipeReader,
        string fragment,
        bool oneway,
        string operation,
        string path,
        PipeReader payload);

    internal delegate IncomingResponse IncomingResponseFactory(
        OutgoingRequest request,
        IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields,
        PipeReader? fieldsPipeReader,
        PipeReader payload,
        ResultType resultType);

    internal interface IProtocolConnectionFactory<T> where T : INetworkConnection
    {
        /// <summary>Creates a protocol connection from a network connection.</summary>
        Task<IProtocolConnection> CreateProtocolConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation connectionInformation,
            IncomingRequestFactory incomingRequestFactory,
            IncomingResponseFactory incomingResponseFactory,
            Configure.ConnectionOptions connectionOptions,
            bool isServer,
            CancellationToken cancel);
    }
}
