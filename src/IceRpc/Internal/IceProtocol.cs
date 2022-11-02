// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>The Ice protocol class.</summary>
internal sealed class IceProtocol : Protocol
{
    /// <summary>Gets the Ice protocol singleton.</summary>
    internal static IceProtocol Instance { get; } = new();

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
                $"{nameof(DecodeDispatchExceptionAsync)} requires a response with a {this} protocol",
                nameof(response));
        }

        ISliceFeature feature = request.Features.Get<ISliceFeature>() ?? SliceFeature.Default;

        // TODO: we probably don't need a segment here.
        ReadResult readResult = await response.Payload.ReadSegmentAsync(
            SliceEncoding.Slice1,
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
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);
            ReplyStatus replyStatus = decoder.DecodeReplyStatus();

            if (replyStatus <= ReplyStatus.UserException)
            {
                throw new InvalidDataException($"invalid system exception with {replyStatus} ReplyStatus");
            }

            string? message = null;
            DispatchErrorCode errorCode;

            switch (replyStatus)
            {
                case ReplyStatus.FacetNotExistException:
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    var requestFailed = new RequestFailedExceptionData(ref decoder);

                    errorCode = replyStatus == ReplyStatus.OperationNotExistException ?
                        DispatchErrorCode.OperationNotFound : DispatchErrorCode.ServiceNotFound;

                    if (requestFailed.Operation.Length > 0)
                    {
                        string target = requestFailed.Fragment.Length > 0 ?
                            $"{requestFailed.Path}#{requestFailed.Fragment}" : requestFailed.Path;

                        message = $"{nameof(DispatchException)} {{ ErrorCode = {errorCode} }} while dispatching '{requestFailed.Operation}' on '{target}'";
                    }
                    // else message remains null
                    break;

                default:
                    message = decoder.DecodeString();
                    errorCode = DispatchErrorCode.UnhandledException;

                    // Attempt to parse the DispatchErrorCode from the message:
                    if (message.StartsWith('[') &&
                        message.IndexOf(']', StringComparison.Ordinal) is int pos && pos != -1)
                    {
                        try
                        {
                            errorCode = (DispatchErrorCode)ulong.Parse(
                                message[1..pos],
                                CultureInfo.InvariantCulture);

                            message = message[(pos + 1)..].TrimStart();
                        }
                        catch
                        {
                            // ignored, keep default errorCode
                        }
                    }
                    break;
            }

            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return new DispatchException(message, errorCode)
            {
                ConvertToUnhandled = true,
                Origin = request
            };
        }
    }

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

    private IceProtocol()
        : base(
            name: "ice",
            defaultPort: 4061,
            hasFields: false,
            hasFragment: true,
            byteValue: 1,
            sliceEncoding: SliceEncoding.Slice1)
    {
    }
}
