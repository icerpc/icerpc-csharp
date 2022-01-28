// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>An assembly attribute for assemblies that contain Slice generated code.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class SliceAttribute : Attribute
    {
        /// <summary>The name of the file that contains the Slice definitions.</summary>
        public string SourceFileName { get; }

        /// <summary>Constructs a new instance of <see cref="SliceAttribute" />.</summary>
        /// <param name="sourceFileName">The name of the source file.</param>
        public SliceAttribute(string sourceFileName) => SourceFileName = sourceFileName;
    }
}
