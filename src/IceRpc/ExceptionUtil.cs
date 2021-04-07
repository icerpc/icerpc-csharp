// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace IceRpc
{
    internal static class ExceptionUtil
    {
        /// <summary>Rethrows an exception while preserving its stack trace. This method does not return and
        /// is typically called as <code>throw ExceptionUtil.Throw(ex);</code>.</summary>
        internal static Exception Throw(Exception ex)
        {
            ExceptionDispatchInfo.Throw(ex);
            Debug.Assert(false);
            return ex;
        }
    }
}
