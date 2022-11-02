// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    internal IMultiplexedStreamErrorCodeConverter MultiplexedStreamErrorCodeConverter { get; }
        = new ErrorCodeConverter();

    public override async ValueTask<DispatchException> DecodeDispatchExceptionAsync(
        IncomingResponse response,
        OutgoingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (response.ResultType != ResultType.Failure)
        {
            throw new ArgumentException(
                $"{nameof(DecodeDispatchExceptionAsync)} requires a response with a Failure result type",
                nameof(response));
        }

        if (response.Protocol != this)
        {
            throw new ArgumentException(
                $"{nameof(DecodeDispatchExceptionAsync)} requires an {this} response",
                nameof(response));
        }

        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            SliceEncoding.Slice2,
            feature.MaxSegmentSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("unexpected call to CancelPendingRead on a response payload");
        }

        DispatchException exception = Decode(readResult.Buffer);
        response.Payload.AdvanceTo(readResult.Buffer.End);
        return exception;

        DispatchException Decode(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            string message = decoder.DecodeString();
            DispatchErrorCode errorCode = decoder.DecodeDispatchErrorCode();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return new DispatchException(message, errorCode)
            {
                ConvertToUnhandled = true,
                Origin = request
            };
        }
    }

    private IceRpcProtocol()
        : base(
            name: "icerpc",
            defaultPort: 4062,
            hasFields: true,
            hasFragment: false,
            byteValue: 2)
    {
    }

    private class ErrorCodeConverter : IMultiplexedStreamErrorCodeConverter
    {
        // We don't map error codes received from the peer to exceptions such as OperationCanceledException,
        // ConnectionException or InvalidData as it would be confusing: OperationCanceledException etc. are local
        // exceptions that don't report remote events.
        public Exception? FromErrorCode(ulong errorCode) =>
            (IceRpcStreamErrorCode)errorCode switch
            {
                IceRpcStreamErrorCode.NoError => null,
                _ => new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode)
            };

        public ulong ToErrorCode(Exception? exception) =>
            exception switch
            {
                null => (ulong)IceRpcStreamErrorCode.NoError,

                OperationCanceledException => (ulong)IceRpcStreamErrorCode.Canceled,

                ConnectionException connectionException =>
                    connectionException.ErrorCode.IsClosedErrorCode() ?
                        (ulong)IceRpcStreamErrorCode.ConnectionShutdown :
                        (ulong)IceRpcStreamErrorCode.Unspecified,

                IceRpcProtocolStreamException streamException => (ulong)streamException.ErrorCode,

                InvalidDataException => (ulong)IceRpcStreamErrorCode.InvalidData,

                _ => (ulong)IceRpcStreamErrorCode.Unspecified
            };
    }
}
