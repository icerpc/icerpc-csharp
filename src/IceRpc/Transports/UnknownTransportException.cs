// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This exception reports that the provided transport is unknown or unregistered.</summary>
    public class UnknownTransportException : NotSupportedException
    {
        /// <summary>Constructs a new instance of the <see cref="UnknownTransportException"/> class.</summary>
        /// <param name="transport">The name of the transport.</param>
        /// <param name="protocol">The Ice protocol.</param>
        public UnknownTransportException(string transport, Protocol protocol)
            : base($"cannot find transport '{transport}' for protocol {protocol.GetName()}")
        {
        }
    }
}
