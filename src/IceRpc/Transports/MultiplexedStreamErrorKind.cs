// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>The error kind for MultiplexedStreamAbortedException errors.</summary>
    /// TODO: can middlewares/interceptors use specific error kinds? Should this eventually be part of the IceRpc core
    /// instead of the transport API?
    public enum MultiplexedStreamErrorKind : int
    {
        /// <summary>Tranport errors.</summary>
        Transport,
        /// <summary>Protocol errors.</summary>
        Protocol,
        /// <summary>Application errors.</summary>
        Application
    }
}
