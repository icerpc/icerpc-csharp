// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// The RetryAbility is carried by remote exceptions to indicate the ability for retrying
    enum Retryable : byte
    {
        /// do not retry
        No,
        /// retry same endpoint after delay ms
        AfterDelay,
        /// retry another replica known to the caller (if any)
        OtherReplica,
    }
}
