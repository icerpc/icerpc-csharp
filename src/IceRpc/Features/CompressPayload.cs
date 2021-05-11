// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature that specifies whether or not the 2.0 encoded payload of a request or response must be
    /// compressed.</summary>
    public sealed class CompressPayload
    {
        /// <summary>A <see cref="CompressPayload"/> instance that specifies that the 2.0 encoded payload of a request
        /// or response must not be compressed.</summary>
        public static CompressPayload No = new CompressPayload();

        /// <summary>A <see cref="CompressPayload"/> instance that specifies that the 2.0 encoded payload of a request
        /// or response must be compressed.</summary>
        public static CompressPayload Yes = new CompressPayload();

        private CompressPayload()
        {
        }
    }

    public static class CompressPayloadExtensions
    {
        public static FeatureCollection CompressPayload(this FeatureCollection features)
        {
            if (features[typeof(Features.CompressPayload)] != Features.CompressPayload.Yes)
            {
                if (features.IsReadOnly)
                {
                    features = new FeatureCollection(features);
                }
                features.Set(Features.CompressPayload.Yes);
            }
            return features;
        }
    }
}
