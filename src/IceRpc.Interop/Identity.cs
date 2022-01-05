// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;

namespace IceRpc
{
    public readonly partial record struct Identity
    {
        /// <summary>Creates an Ice identity from a URI path.</summary>
        /// <param name="path">A URI path.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is not a valid path.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="path"/> is a valid path but cannot be
        /// converted into an identity.</exception>
        /// <returns>A new Ice identity struct.</returns>
        public static Identity FromPath(string path)
        {
            var identity = Slice.Internal.Identity.FromPath(path);
            return new(identity.Name, identity.Category);
        }

        /// <summary>Creates an Identity from a string in the ice format.</summary>
        /// <param name="s">A "stringified identity" in the ice format.</param>
        /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not in the correct format.
        /// </exception>
        /// <returns>A new Identity struct.</returns>
        public static Identity Parse(string s)
        {
            // Find unescaped separator. Note that the string may contain an escaped backslash before the separator.
            int slash = -1, pos = 0;
            while ((pos = s.IndexOf('/', pos)) != -1)
            {
                int escapes = 0;
                while (pos - escapes > 0 && s[pos - escapes - 1] == '\\')
                {
                    escapes++;
                }

                // We ignore escaped escapes
                if (escapes % 2 == 0)
                {
                    if (slash == -1)
                    {
                        slash = pos;
                    }
                    else
                    {
                        // Extra unescaped slash found.
                        throw new FormatException($"unescaped backslash in identity '{s}'");
                    }
                }
                pos++;
            }

            string category;
            string? name = null;
            if (slash == -1)
            {
                try
                {
                    name = StringUtil.UnescapeString(s, 0, s.Length, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid name in identity '{s}'", ex);
                }
                category = "";
            }
            else
            {
                try
                {
                    category = StringUtil.UnescapeString(s, 0, slash, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid category in identity '{s}'", ex);
                }

                if (slash + 1 < s.Length)
                {
                    try
                    {
                        name = StringUtil.UnescapeString(s, slash + 1, s.Length, "/");
                    }
                    catch (ArgumentException ex)
                    {
                        throw new FormatException($"invalid name in identity '{s}'", ex);
                    }
                }
            }

            return name?.Length > 0 ? new Identity(name, category) :
                throw new FormatException($"invalid empty name in identity '{s}'");
        }

        /// <summary>Attempts to create an Identity from string in the ice format.</summary>
        /// <param name="s">A "stringified identity" in the ice format</param>
        /// <param name="identity">When this method succeeds, contains an Identity struct parsed from s.</param>
        /// <returns>True if <paramref name="s"/> was parsed successfully; otherwise, false.</returns>
        public static bool TryParse(string s, out Identity identity)
        {
            try
            {
                identity = Parse(s);
                return true;
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        /// <summary>Converts this identity into a URI path.</summary>
        /// <returns>A URI path.</returns>
        public string ToPath() => new Slice.Internal.Identity(Name, Category).ToPath();

        /// <inheritdoc/>
        public override string ToString() => ToString(IceProxyFormat.Default);

        /// <summary>Converts this identity into a string, using the format specified by ToStringMode.</summary>
        /// <param name="format">Specifies how non-printable ASCII characters are escaped in the resulting string. See
        /// <see cref="IceProxyFormat"/>.</param>
        /// <returns>The string representation of this identity.</returns>
        public string ToString(IceProxyFormat format)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return "";
            }
            Debug.Assert(Category != null);

            string escapedName = StringUtil.EscapeString(Name, format.EscapeMode, '/');

            if (Category.Length == 0)
            {
                return escapedName;
            }
            else
            {
                string escapedCategory = StringUtil.EscapeString(Category, format.EscapeMode, '/');
                return $"{escapedCategory}/{escapedName}";
            }
        }
    }
}
