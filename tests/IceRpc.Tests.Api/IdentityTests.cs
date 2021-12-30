// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class IdentityTests
    {
        /// <summary>Verifies that a URI path can be converted to an identity.</summary>
        /// <param name="path">The path to check.</param>
        /// <param name="normalizedPath">The path returned by ToPath if different than path.</param>
        [TestCase("/foo/bar")]
        [TestCase("/foo/bar")]
        [TestCase("/foo:foo/bar$bar", "/foo%3Afoo/bar%24bar")]
        [TestCase("/")]
        [TestCase("/foo/")]
        [TestCase("//foo", "/foo")]
        [TestCase("//", "/")]
        public void Identity_FromPathToPath(string path, string? normalizedPath = null)
        {
            var identity = Identity.FromPath(path);
            Assert.AreEqual(normalizedPath ?? path, identity.ToPath());
        }

        /// <summary>Identity.FromPath for an invalid path throws ArgumentException.</summary>
        [TestCase("foo/bar/abc")] // does not start with a slash
        public void Identity_FromPath_ArgumentException(string path) =>
            Assert.Throws<ArgumentException>(() => Identity.FromPath(path));

        /// <summary>Identity.FromPath for a valid path that can't be converted to an identity throws FormatException.
        /// </summary>
        [TestCase("/foo/bar/abc")] // too many slashes
        [TestCase("///")] // too many slashes
        public void Identity_FromPath_FormatException(string path) =>
            Assert.Throws<FormatException>(() => Identity.FromPath(path));

        /// <summary>Verifies that simple stringified identities result in the same identity with Parse and FromPath
        /// when the leading / is removed for Parse.</summary>
        [TestCase("/foo", "foo", "")]
        [TestCase("/foo/bar", "bar", "foo")]
        [TestCase("/foo/bar+", "bar+", "foo")]
        public void Identity_FromSimpleString(string str, string name, string category)
        {
            var identity = new Identity(name, category);
            Assert.AreEqual(identity, Identity.Parse(str[1..]));
            Assert.AreEqual(identity, Identity.FromPath(str));
        }

        /// <summary>Identity.Parse for an invalid identity throws FormatException, Identity.TryParse
        /// for an invalid identity must return false.</summary>
        [TestCase("xx\01FooBar")] // Illegal character < 32
        [TestCase("xx\\ud911")] // Illegal surrogate
        [TestCase("test/foo/bar")]
        [TestCase("cat//test")]
        [TestCase("")] // Empty name
        [TestCase("cat/")] // Empty name
        public void Identity_Parse_InvalidInput(string str)
        {
            Assert.Throws<FormatException>(() => Identity.Parse(str));
            Assert.That(Identity.TryParse(str, out _), Is.False);
        }

        /// <summary>Verifies Identity.Parse succeeds and constructs the expected identity.</summary>
        [TestCase("\\342\\x82\\254\\60\\x9\\60\\", "€0\t0\\", "")]
        public void Identity_Parse_ValidInput(string str, string name, string category) =>
            Assert.AreEqual(new Identity(name, category), Identity.Parse(str));

        /// <summary>Verifies that any arbitrary Identity can represented by a URI path (i.e. produced from FromPath)
        /// and that ToPath then returns the same path.</summary>
        /// <param name="name">The name field of the Identity.</param>
        /// <param name="category">The category field of the Identity.</param>
        /// <param name="path">The normalized URI path to check against.</param>
        [TestCase("foo", "bar", "/bar/foo")]
        [TestCase("foo", "", "/foo")]
        [TestCase("test", "\x7f€", "/%7F%E2%82%AC/test")]
        [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                  "greek \ud800\udd6a",
                  "/greek%20%F0%90%85%AA/banana%20%0E-%F0%9F%8D%8C%E2%82%AC%C2%A2%24")]
        [TestCase("/foo", "", "/%2Ffoo")]
        [TestCase("/foo", "bar", "/bar/%2Ffoo")]
        [TestCase("/foo", "/bar/", "/%2Fbar%2F/%2Ffoo")]
        [TestCase("foo/// ///#@", "/bar/", "/%2Fbar%2F/foo%2F%2F%2F%20%2F%2F%2F%23%40")]
        [TestCase("", "", "/")] // empty identity
        [TestCase("", "cat/", "/cat%2F/")] // category with trailing slash and empty name
        public void Identity_ToPathFromPath(string name, string category, string path)
        {
            var identity = new Identity(name, category);
            Assert.AreEqual(identity, Identity.FromPath(identity.ToPath()));
            Assert.AreEqual(path, identity.ToPath());
        }

        /// <summary>Verifies that Identity can be converted to a string (with ToString) and converted back to the same
        /// identity (with Parse).</summary>
        /// <param name="name">The name field of the Identity.</param>
        /// <param name="category">The category field of the Identity.</param>
        /// <param name="unicode">The "stringified" unicode identity to check against.</param>
        /// <param name="ascii">The "stringified" ascii identity to check against (optional).</param>
        /// <param name="compat">The "stringified" compat identity to check against (optional).</param>

        [TestCase("test", "\x7f€", "\\u007f€/test", "\\u007f\\u20ac/test", "\\177\\342\\202\\254/test")]
        [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                  "greek \ud800\udd6a",
                  "greek \ud800\udd6a/banana \\u000e-\ud83c\udf4c\u20ac\u00a2$",
                  "greek \\U0001016a/banana \\u000e-\\U0001f34c\\u20ac\\u00a2$",
                  "greek \\360\\220\\205\\252/banana \\016-\\360\\237\\215\\214\\342\\202\\254\\302\\242$")]
        [TestCase("test", ",X2QNUAzSBcJ_e$AV;E\\", ",X2QNUAzSBcJ_e$AV;E\\\\/test")]
        [TestCase("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\", ",X2QNUAz\\\\SB\\\\\\/cJ_e$AV;E\\\\\\\\/test")]
        [TestCase("/test", "cat/", "cat\\//\\/test")]
        public void Identity_ToStringParse(
            string name,
            string category,
            string unicode,
            string ascii = "",
            string compat = "")
        {
            var identity = new Identity(name, category);

            foreach (IceProxyFormat format in
                ImmutableList.Create(IceProxyFormat.Unicode, IceProxyFormat.ASCII, IceProxyFormat.Compat))
            {
                Assert.AreEqual(identity, Identity.Parse(identity.ToString(format)));

                Assert.That(Identity.TryParse(identity.ToString(format), out Identity newIdentity), Is.True);
                Assert.AreEqual(identity, newIdentity);
            }

            Assert.AreEqual(unicode, identity.ToString(IceProxyFormat.Unicode));
            if (ascii.Length > 0)
            {
                Assert.AreEqual(ascii, identity.ToString(IceProxyFormat.ASCII));
            }
            if (compat.Length > 0)
            {
                Assert.AreEqual(compat, identity.ToString(IceProxyFormat.Compat));
            }
        }
    }
}
