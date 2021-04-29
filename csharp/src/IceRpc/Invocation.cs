// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Holds properties to customize a request and to get back information from the corresponding response.
    /// </summary>
    public sealed class Invocation
    {
        public FormatType ClassFormat { get; set; }

        // temporary
        public bool CompressRequestPayload { get; set; }

        /// <summary>The context dictionary carried by the request.</summary>
        public SortedDictionary<string, string> Context { get; set; } = new();

        public bool IsIdempotent { get; set; }

        /// <summary>When true and the operation returns void, the request is sent as a oneway request. Otherwise, the
        /// request is sent as a two-way request.</summary>
        public bool IsOneway { get; set; }

        /// <summary>The progress provider.</summary>
        public IProgress<bool>? Progress { get; set; }
    }
}
