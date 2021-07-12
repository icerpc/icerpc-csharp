// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to mark operations that can be called from
    /// Service.DispatchAsync.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OperationAttribute : Attribute
    {
        /// <summary>Retrieves the operation name.</summary>
        /// <value>The operation name.</value>
        public string Value { get; }

        /// <summary>Constructs a OperationAttribute.</summary>
        /// <param name="value">The operation name.</param>>
        public OperationAttribute(string value) => Value = value;
    }
}
