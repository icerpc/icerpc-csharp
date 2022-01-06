// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Holds properties to customize a request and to get back information from the corresponding response.
    /// </summary>
    /// <remarks>This Slice class is in the main IceRpc namespace because it's used as a parameter in Prx methods
    /// generated by the Slice compiler.</remarks>
    public sealed class Invocation
    {
        /// <summary>Gets or sets the value of the Context feature in <see cref="RequestFeatures"/>.</summary>
        public IDictionary<string, string> Context
        {
            get => RequestFeatures.GetContext();
            set => RequestFeatures = RequestFeatures.WithContext(value);
        }

        /// <summary>Gets or sets the deadline of this invocation.</summary>
        /// <value>The deadline of this invocation. The default value is <see cref="DateTime.MaxValue"/> and means no
        /// deadline.</value>
        public DateTime Deadline { get; set; } = DateTime.MaxValue;

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

        /// <summary>Gets or sets the timeout of this invocation.</summary>
        /// <value>The timeout of this invocation. The default value is
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> and means no timeout.</value>
        /// <seealso cerf="TimeoutInterceptor"/>
        public TimeSpan Timeout
        {
            get => _timeout;
            set => _timeout = value > TimeSpan.Zero || value == System.Threading.Timeout.InfiniteTimeSpan ? value :
                throw new ArgumentException($"{nameof(Timeout)} must be greater than 0", nameof(Timeout));
        }

        private TimeSpan _timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}
