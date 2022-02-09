// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extensions methods to encodes and decode a path into/from an Ice Identity.</summary>
    public static class IdentityPathExtensions // TODO: see https://github.com/zeroc-ice/icerpc-csharp/issues/786
    {
        /// <summary>Decodes a path from an identity representation.</summary>
        public static string DecodeIdentityPath(this ref SliceDecoder decoder) =>
            ToPath(decoder.DecodeString(), decoder.DecodeString());

        /// <summary>Encodes a path as an identity.</summary>
        public static void EncodeIdentityPath(this ref SliceEncoder encoder, string value)
        {
            (string name, string category) = FromPath(value);
            encoder.EncodeString(name);
            encoder.EncodeString(category);
        }

        /// <summary>Converts a path into an identity (name-category pair).</summary>
        /// <param name="path">The path (percent escaped).</param>
        /// <returns>The corresponding identity.</returns>
        internal static (string Name, string Category) FromPath(string path)
        {
            string workingPath = path[1..]; // removes leading /.

            int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);

            string name;
            string category = "";

            if (firstSlash == -1)
            {
                // Name only
                name = Uri.UnescapeDataString(workingPath);
            }
            else
            {
                if (firstSlash != workingPath.LastIndexOf('/'))
                {
                    throw new FormatException($"too many slashes in path '{path}'");
                }

                name = Uri.UnescapeDataString(workingPath[(firstSlash + 1)..]);
                category = Uri.UnescapeDataString(workingPath[0..firstSlash]);
            }

            if (name.Length == 0)
            {
                // null identity
                category = "";
            }

            return (name, category);
        }

        /// <summary>Converts an identity (name-category) pair into a path.</summary>
        /// <param name="name">The name (not percent escaped).</param>
        /// <param name="category">The category (not percent escaped).</param>
        /// <returns>The corresponding path.</returns>
        internal static string ToPath(string name, string category)
        {
            if (name.Length == 0)
            {
                return "/";
            }

            return category.Length > 0 ?
                $"/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(name)}" :
                $"/{Uri.EscapeDataString(name)}";
        }
    }
}
