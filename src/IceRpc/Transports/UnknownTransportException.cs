// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc.Transports
{
    /// <summary>This exception reports that the provided transport is unknown or unregistered.</summary>
    public class UnknownTransportException : NotSupportedException
    {
        /// <summary>Constructs a new instance of the <see cref="UnknownTransportException"/> class.</summary>
        /// <param name="transport">The transport code.</param>
        public UnknownTransportException(TransportCode transport)
            : base($"unknown transport {transport}")
        {
        }

        /// <summary>Constructs a new instance of the <see cref="UnknownTransportException"/> class.</summary>
        /// <param name="transport">The transport.</param>
        public UnknownTransportException(TransportId transport)
            : base($"unknown transport {transport}")
        {
        }
    }
}
