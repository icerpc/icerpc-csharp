// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice.Internal
{
    internal readonly partial record struct Identity
    {
        /// <summary>The empty identity.</summary>
        internal static readonly Identity Empty = new("", "");

        /// <summary>Creates an Ice identity from a URI path.</summary>
        /// <param name="uriPath">A URI absolute path. The caller guarantees it's properly escaped.</param>
        /// <exception cref="FormatException">Thrown when <paramref name="uriPath"/> cannot be converted into a non-null
        /// identity.</exception>
        /// <returns>A new Ice identity struct.</returns>
        internal static Identity FromPath(string uriPath)
        {
            string workingPath = uriPath[1..]; // removes leading /.

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
                    throw new FormatException($"too many slashes in path '{uriPath}'");
                }

                name = Uri.UnescapeDataString(workingPath[(firstSlash + 1)..]);
                category = Uri.UnescapeDataString(workingPath[0..firstSlash]);
            }

            return name.Length > 0 ? new Identity(name, category) :
                throw new FormatException($"invalid empty identity name in '{uriPath}'");
        }

        /// <summary>Converts this identity into a URI path.</summary>
        /// <returns>A URI path.</returns>
        public string ToPath()
        {
            // Name is null when this identity is default initialized
            if (Name == null || Name.Length == 0)
            {
                throw new InvalidOperationException("cannot create a path from an identity with an empty name");
            }
            Debug.Assert(Category != null);

            string path = Category.Length > 0 ?
                $"/{Uri.EscapeDataString(Category)}/{Uri.EscapeDataString(Name)}" :
                $"/{Uri.EscapeDataString(Name)}";

            return path;
        }

        /// <inheritdoc/>
        public override string ToString() => Name == null || Name.Length == 0 ? "" : ToPath();
    }
}
