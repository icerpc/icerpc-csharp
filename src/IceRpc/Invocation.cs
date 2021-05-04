// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Holds properties to customize a request and to get back information from the corresponding response.
    /// </summary>
    public sealed class Invocation
    {
        /// <summary>Gets or sets the marshaling format for classes.</summary>
        public FormatType ClassFormat { get; set; }

        // temporary
        public bool CompressRequestPayload { get; set; }

        /// <summary>Gets or sets the context dictionary carried by the request.</summary>
        public SortedDictionary<string, string> Context { get; set; } = new();

        /// <summary>Gets or sets the deadline of this invocation. When null or set to <see cref="DateTime.MaxValue"/>,
        /// <see cref="Proxy.InvokeAsync"/> uses <see cref="Timeout"/> to create (and enforce) a deadline for the
        /// invocation.</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>Gets or sets whether the caller considers the implementation of the target operation idempotent.
        /// </summary>
        public bool IsIdempotent { get; set; }

        /// <summary>Gets or sets whether a void-returning request is oneway. When true, the request is sent as a
        /// oneway request. Otherwise, the request is sent as a twoway request.</summary>
        public bool IsOneway { get; set; }

        /// <summary>Gets or sets the progress provider.</summary>
        public IProgress<bool>? Progress { get; set; }

        /// <summary>Gets or sets the features carried by the request.</summary>
        public FeatureCollection RequestFeatures { get; set; } = new FeatureCollection();

        /// <summary>Gets or sets the features carried by the response.</summary>
        public FeatureCollection ResponseFeatures { get; set; } = new FeatureCollection();

        /// <summary>Gets or sets the timeout of this invocation. The null value is equivalent to the proxy's invocation
        /// timeout. <see cref="Proxy.InvokeAsync"/> creates a deadline from this timeout unless <see cref="Deadline"/>
        /// is null or set to <see cref="DateTime.MaxValue"/>.</summary>
        public TimeSpan? Timeout
        {
            get => _timeout;
            set => _timeout = (value == null || value.Value != TimeSpan.Zero) ? value :
                throw new ArgumentException("zero is not a valid timeout value", nameof(Timeout));
        }

        private TimeSpan? _timeout;
    }
}
