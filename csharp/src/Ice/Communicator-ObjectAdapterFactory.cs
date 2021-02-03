// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    public sealed partial class Communicator
    {
        /// <summary>Creates a new object adapter.</summary>
        /// <param name="name">The object adapter name. A unique name is generated if an empty string is specified.</param>
        /// <param name="options">The object adapter options.</param>
        /// <param name="serializeDispatch">Indicates whether or not this object adapter serializes the dispatching of
        /// of requests received over the same connection.</param>
        /// <param name="taskScheduler">The optional task scheduler to use for dispatching requests.</param>
        /// <returns>The new object adapter.</returns>
        public ObjectAdapter CreateObjectAdapter(
            string name = "",
            ObjectAdapterOptions? options = null,
            bool serializeDispatch = false,
            TaskScheduler? taskScheduler = null) =>
            new(this, name, options, serializeDispatch, taskScheduler);
    }
}
