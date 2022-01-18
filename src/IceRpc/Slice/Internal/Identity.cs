// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice.Internal
{
    internal readonly partial record struct Identity
    {
        /// <summary>The empty identity.</summary>
        internal static readonly Identity Empty = new("", "");

        /// <summary>Creates an Ice identity from a URI path.</summary>
        /// <param name="path">A URI path.</param>
        /// <exception cref="FormatException">Thrown when <paramref name="path"/> is a not a valid path or cannot be
        /// converted into an identity.</exception>
        /// <returns>A new Ice identity struct.</returns>
        internal static Identity FromPath(string path)
        {
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

            return path;
        }

        /// <inheritdoc/>
        public override string ToString() => ToPath();
    }
}
