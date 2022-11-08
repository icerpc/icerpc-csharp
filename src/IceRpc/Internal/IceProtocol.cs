// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

    /// <summary>Checks if this absolute path holds a valid identity.</summary>
    internal override void CheckPath(string uriPath)
    {
        string workingPath = uriPath[1..]; // removes leading /.
        int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);

        string escapedName;

        if (firstSlash == -1)
        {
            escapedName = workingPath;
        }
        else
        {
            if (firstSlash != workingPath.LastIndexOf('/'))
            {
                throw new FormatException($"too many slashes in path '{uriPath}'");
            }
            escapedName = workingPath[(firstSlash + 1)..];
        }

        if (escapedName.Length == 0)
        {
            throw new FormatException($"invalid empty identity name in path '{uriPath}'");
        }
    }

    /// <summary>Checks if the service address parameters are valid. The only valid parameter is adapter-id with a
    /// non-empty value.</summary>
    internal override void CheckServiceAddressParams(ImmutableDictionary<string, string> serviceAddressParams)
    {
        foreach ((string name, string value) in serviceAddressParams)
        {
            if (name == "adapter-id")
            {
                if (value.Length == 0)
                {
                    throw new FormatException("the value of the adapter-id parameter cannot be empty");
                }
            }
            else
            {
                throw new FormatException($"'{name}' is not a valid ice service address parameter");
            }
        }
    }

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

        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            SliceEncoding.Slice1,
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
            exception = Decode(readResult.Buffer, response.StatusCode);
        }
        catch
        {
            response.Payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            throw;
        }

        response.Payload.AdvanceTo(readResult.Buffer.End);
        return exception;

        DispatchException Decode(ReadOnlySequence<byte> buffer, StatusCode statusCode)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);
            ReplyStatus replyStatus = decoder.DecodeReplyStatus();

            if (replyStatus <= ReplyStatus.UserException)
            {
                throw new InvalidDataException($"invalid system exception with {replyStatus} ReplyStatus");
            }

            string message;
            switch (replyStatus)
            {
                case ReplyStatus.FacetNotExistException:
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    var requestFailed = new RequestFailedExceptionData(ref decoder);

                    string target = requestFailed.Fragment.Length > 0 ?
                        $"{requestFailed.Path}#{requestFailed.Fragment}" : requestFailed.Path;

                    message = $"{nameof(DispatchException)} {{ StatusCode = {statusCode} }} while dispatching '{requestFailed.Operation}' on '{target}'";
                    break;

                case ReplyStatus.UnknownException:
                    message = IceProtocolConnection.ParseUnknownExceptionMessage(decoder.DecodeString()).Message;
                    break;

                default:
                    message = decoder.DecodeString();
                    break;
            }

            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return new DispatchException(message, statusCode)
            {
                ConvertToUnhandled = true,
                Origin = request
            };
        }
    }

    private IceProtocol()
        : base(
            name: "ice",
            defaultPort: 4061,
            hasFields: false,
            hasFragment: true,
            byteValue: 1)
    {
    }
}
