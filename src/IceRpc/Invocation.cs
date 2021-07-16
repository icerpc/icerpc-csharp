// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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

        /// <summary>Gets or sets the value of the Context feature in <see cref="RequestFeatures"/>.</summary>
        public IDictionary<string, string> Context
        {
            get => RequestFeatures.GetContext();
            set => RequestFeatures = RequestFeatures.WithContext(value);
        }

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

        /// <summary>Gets or sets the features carried by the request.</summary>
        public FeatureCollection RequestFeatures { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the features carried by the response.</summary>
        public FeatureCollection ResponseFeatures { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the timeout of this invocation. The conversion of this invocation into an
        /// <see cref="OutgoingRequest"/> creates a deadline from this timeout when <see cref="Deadline"/> is null or
        /// set to <see cref="DateTime.MaxValue"/>.</summary>
        /// <value>The timeout of this invocation.</value>
        public TimeSpan? Timeout
        {
            get => _timeout;
            set => _timeout = value > TimeSpan.Zero || value == System.Threading.Timeout.InfiniteTimeSpan ? value :
                throw new ArgumentException($"{nameof(Timeout)} must be greater than 0", nameof(Timeout));
        }

        private TimeSpan? _timeout;
    }
}
