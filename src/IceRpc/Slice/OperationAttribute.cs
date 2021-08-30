// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>This attribute class is used by the generated code to mark operations that can be called from
    /// Service.DispatchAsync.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OperationAttribute : Attribute
    {
        /// <summary>Retrieves the operation name.</summary>
        /// <value>The operation name.</value>
        public string Value { get; }

        /// <summary>Constructs an OperationAttribute.</summary>
        /// <param name="value">The operation name.</param>>
        public OperationAttribute(string value) => Value = value;
    }
}
