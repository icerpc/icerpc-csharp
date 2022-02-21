// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    internal readonly partial record struct Identity
    {
        /// <summary>The null identity.</summary>
        internal static Identity Empty { get; } = new("", "");

        public override string ToString() => ToPath();

        /// <summary>Parses a path into an identity.</summary>
        /// <param name="path">The path (percent escaped).</param>
        /// <returns>The corresponding identity. Its name can be empty.</returns>
        internal static Identity Parse(string path)
        {
            if (!path.StartsWith('/'))
            {
                throw new FormatException("path must start with a /");
            }

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

            return name.Length == 0 ? Empty : new(name, category);
        }

        /// <summary>Converts this identity into a path.</summary>
        internal string ToPath()
        {
            if (Name.Length == 0)
            {
                return "/";
            }

            return Category.Length > 0 ?
                $"/{Uri.EscapeDataString(Category)}/{Uri.EscapeDataString(Name)}" :
                $"/{Uri.EscapeDataString(Name)}";
        }
    }
}
