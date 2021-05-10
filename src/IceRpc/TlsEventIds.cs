// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains constants used for Tls logging event Ids.</summary>
    public static class TlsEventIds
    {
        public static readonly EventId TlsAuthenticationSucceeded =
            new(BaseEventId + 0, nameof(TlsAuthenticationSucceeded));

        public static readonly EventId TlsAuthenticationFailed =
            new(BaseEventId + 1, nameof(TlsAuthenticationFailed));

        private const int BaseEventId = Internal.LoggerExtensions.TlsBaseEventId;
    }
}
