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
        public IDictionary<string, string>? Context { get; set; }

        /// <summary>Gets or sets the deadline of this invocation.</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>Gets or sets whether the caller considers the implementation of the target operation idempotent.
        /// </summary>
        public bool IsIdempotent { get; set; }

        /// <summary>Gets or sets whether a void-returning request is oneway. This property has no effect for operations
        /// defined in Slice that return a value.</summary>
        /// <value>When <c>true</c>, the request is sent as a oneway request. When <c>false</c>, the request is sent as
        /// a twoway request unless the operation is marked oneway in its Slice definition.</value>
        public bool IsOneway { get; set; }

        /// <summary>Gets or sets the progress provider.</summary>
        public IProgress<bool>? Progress { get; set; }

        /// <summary>Gets or sets the features carried by the request.</summary>
        public FeatureCollection RequestFeatures { get; set; } = new FeatureCollection();

        /// <summary>Gets or sets the features carried by the response.</summary>
        public FeatureCollection ResponseFeatures { get; set; } = new FeatureCollection();

        /// <summary>Gets or sets the timeout of this invocation. The conversion of this invocation into an
        /// <see cref="OutgoingRequest"/> creates a deadline from this timeout when <see cref="Deadline"/> is null or
        /// set to <see cref="DateTime.MaxValue"/>.</summary>
        /// <value>The timeout of this invocation. Its default value is <see cref="Runtime.DefaultInvocationTimeout"/>.
        /// </value>
        public TimeSpan Timeout
        {
            get => _timeout ?? Runtime.DefaultInvocationTimeout;
            set => _timeout = (value != TimeSpan.Zero) ? value :
                throw new ArgumentException("zero is not a valid timeout value", nameof(Timeout));
        }

        private TimeSpan? _timeout;
    }
}
