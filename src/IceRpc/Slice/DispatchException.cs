// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Text;

namespace IceRpc.Slice
{
    // Extends partial class DispatchException generated by the Slice compiler.
    public partial class DispatchException
    {
        /// <inheritdoc/>
        protected override string? DefaultMessage
        {
            get
            {
                switch (ErrorCode)
                {
                    case DispatchErrorCode.ServiceNotFound:
                        if (Origin != RemoteExceptionOrigin.Unknown)
                        {
                            var sb = new StringBuilder("could not find service '");
                            sb.Append(Origin.Path);
                            sb.Append("' while attempting to dispatch operation '");
                            sb.Append(Origin.Operation);
                            sb.Append('\'');
                            return sb.ToString();
                        }
                        break;

                    case DispatchErrorCode.OperationNotFound:
                        if (Origin != RemoteExceptionOrigin.Unknown)
                        {
                            var sb = new StringBuilder("could not find operation '");
                            sb.Append(Origin.Operation);
                            sb.Append("' for service '");
                            sb.Append(Origin.Path);
                            sb.Append('\'');
                            return sb.ToString();
                        }
                        break;

                    default:
                        break;
                }

                string message = $"{nameof(DispatchException)} {{ ErrorCode = {ErrorCode} }}";

                if (Origin != RemoteExceptionOrigin.Unknown)
                {
                    message += $" while dispatching '{Origin.Operation}' on service '{Origin.Path}'";
                }
                if (InnerException != null)
                {
#if DEBUG
                    message += $":\n{InnerException}\n---";
#else
                    // The stack trace of the inner exception can include sensitive information we don't want to
                    // send "over the wire" in non-debug builds.
                    message += $":\n{InnerException.Message}";
#endif
                }
                return message;
            }
        }
    }
}
