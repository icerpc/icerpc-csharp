// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>). All these
    /// properties are inherited when unmarshaling a proxy.</summary>
    public class ProxyOptions
    {
        /// <summary>The invoker.</summary>
        public IInvoker? Invoker { get; set; }

        public ProxyOptions Clone() => (ProxyOptions)MemberwiseClone();
    }
}
