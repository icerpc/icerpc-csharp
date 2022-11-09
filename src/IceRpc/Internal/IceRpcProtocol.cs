// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    internal IPayloadExceptionConverter PayloadErrorCodeConverter { get; } = new IceRpcPayloadExceptionConverter();

    internal override async ValueTask<DispatchException> DecodeDispatchExceptionAsync(
        IncomingResponse response,
        OutgoingRequest request,
        CancellationToken cancellationToken)
    {
        Debug.Assert(response.Protocol == this);

        if (response.StatusCode <= StatusCode.Failure)
        {
            throw new ArgumentOutOfRangeException(
                nameof(response.StatusCode),
                $"{nameof(DecodeDispatchExceptionAsync)} requires a response with a status code greater than {nameof(StatusCode.Failure)}");
        }

        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        // We're actually not reading a segment here but a Slice2-encoded string. It looks like a segment:
        // <size><utf8 bytes>.
        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            SliceEncoding.Slice2,
            feature.MaxSegmentSize,
            cancellationToken).ConfigureAwait(false);

        // We never call CancelPendingRead on a response.Payload; an interceptor can but it's not correct.
        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("unexpected call to CancelPendingRead on a response payload");
        }

        DispatchException exception;
        try
        {
            exception = Decode(readResult.Buffer);
        }
        catch
        {
            response.Payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            throw;
        }

        response.Payload.AdvanceTo(readResult.Buffer.End);
        return exception;

        DispatchException Decode(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            string message = decoder.DecodeStringBody((int)buffer.Length);
            return new DispatchException(message, response.StatusCode)
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

    private class IceRpcPayloadExceptionConverter : IPayloadExceptionConverter
    {
        public ulong FromInputCompleteException(Exception? exception) =>
            exception switch
            {
                null => (ulong)PayloadCompleteErrorCode.Done,
                PayloadCompleteException payloadCompleteException => (ulong)payloadCompleteException.ErrorCode,
                OperationCanceledException => (ulong)PayloadCompleteErrorCode.Canceled,
                InvalidDataException => (ulong)PayloadCompleteErrorCode.InvalidData,
                _ => (ulong)PayloadCompleteErrorCode.Unspecified
            };

        public ulong FromOutputCompleteException(Exception exception) =>
            exception switch
            {
                PayloadReadException payloadReadException => (ulong)payloadReadException.ErrorCode,
                OperationCanceledException => (ulong)PayloadReadErrorCode.Canceled,
                ConnectionException connectionException => (ulong)(connectionException.ErrorCode.IsClosedErrorCode() ?
                    PayloadReadErrorCode.ConnectionShutdown : PayloadReadErrorCode.Unspecified),
                _ => (ulong)PayloadReadErrorCode.Unspecified
            };

        public PayloadCompleteException? ToPayloadCompleteException(ulong errorCode)
        {
            var payloadCompleteErrorCode = (PayloadCompleteErrorCode)errorCode;
            return payloadCompleteErrorCode == PayloadCompleteErrorCode.Done ? null : new(payloadCompleteErrorCode);
        }

        public PayloadReadException ToPayloadReadException(ulong errorCode) => new((PayloadReadErrorCode)errorCode);
    }
}
