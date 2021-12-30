// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;

namespace IceRpc.Slice.Internal
{
    internal readonly partial record struct Identity
    {
        /// <summary>The empty identity.</summary>
        internal static readonly Identity Empty = new("", "");

        /// <summary>Creates an Ice identity from a URI path.</summary>
        /// <param name="path">A URI path.</param>
        /// <exception cref="ArgumentException">path is not a valid path.</exception>
        /// <exception cref="FormatException">path is a valid path but cannot be converted into an identity.</exception>
        /// <returns>A new Ice identity struct.</returns>
        internal static Identity FromPath(string path)
        {
            UriProxyFormat.CheckPath(path, nameof(path));
            string workingPath = path[1..]; // removes leading /.

            int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);
            if (firstSlash != workingPath.LastIndexOf('/'))
            {
                throw new FormatException($"too many slashes in path '{path}'");
            }

            if (firstSlash == -1)
            {
                // Name only
                return new Identity(Uri.UnescapeDataString(workingPath), "");
            }
            else
            {
                return new Identity(
                    Uri.UnescapeDataString(workingPath[(firstSlash + 1)..]),
                    Uri.UnescapeDataString(workingPath[0..firstSlash]));
            }
        }

        /// <summary>Converts this identity into a URI path.</summary>
        /// <returns>A URI path.</returns>
        public string ToPath()
        {
            if (Name == null)
            {
                return "/"; // This struct was default initialized (null)
            }
            Debug.Assert(Category != null);

            string path = Category.Length > 0 ?
                $"/{Uri.EscapeDataString(Category)}/{Uri.EscapeDataString(Name)}" :
                $"/{Uri.EscapeDataString(Name)}";

            Debug.Assert(UriProxyFormat.IsValidPath(path));
            return path;
        }

        /// <inheritdoc/>
        public override string ToString() => ToPath();
    }
}
