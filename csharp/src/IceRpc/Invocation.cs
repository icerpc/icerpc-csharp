// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Holds properties to customize a request and to get back information from the corresponding response.
    /// </summary>
    public sealed class Invocation
    {
        /// <summary>The context dictionary carried by the request.</summary>
        public SortedDictionary<string, string> Context { get; set; } = new();

        /// <summary>The progress provider.</summary>
        public IProgress<bool>? Progress { get; set; }
    }
}
