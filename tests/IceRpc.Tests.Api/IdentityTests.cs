// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class IdentityTests
    {
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
            Assert.False(Identity.TryParse(str, out _));
        }

        /// <summary>Verifies Identity.Parse succeeds and constructs the expected identity.</summary>
        [TestCase("\\342\\x82\\254\\60\\x9\\60\\", "€0\t0\\", "")]
        public void Identity_Parse_ValidInput(string str, string name, string category)
        {
            Assert.AreEqual(new Identity(name, category), Identity.Parse(str));
        }

        /// <summary>Verifies that a URI path can be converted to an identity and identity.ToPath() returns a normalized
        /// version of path.</summary>
        /// <param name="path">The path to check.</param>
        [TestCase("foo/bar")]
        [TestCase("/")]
        [TestCase("/foo/")]
        public void Identity_FromPathToPath(string path)
        {
            var identity = Identity.FromPath(path);
            Assert.AreEqual(Proxy.NormalizePath(path), identity.ToPath());
        }

        /// <summary>Verifies that Identity can be converted to a URI path (with ToPath) and converted back to the same
        /// identity (with FromPath).</summary>
        /// <param name="name">The name field of the Identity.</param>
        /// <param name="category">The category field of the Identity.</param>
        /// <param name="path">The normalized URI path to check against.</param>
        [TestCase("foo", "bar", "/bar/foo")]
        [TestCase("foo", "", "/foo")]
        [TestCase("test", "\x7f€", "/%7F%E2%82%AC/test")]
        [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                  "greek \ud800\udd6a",
                  "/greek%20%F0%90%85%AA/banana%20%0E-%F0%9F%8D%8C%E2%82%AC%C2%A2%24")]
        [TestCase("/foo", "", "///foo")]
        [TestCase("/foo", "bar", "/bar//foo")]
        [TestCase("/foo", "/bar/", "/%2Fbar%2F//foo")]
        [TestCase("foo/// ///#@", "", "//foo///%20///%23%40")]
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

            foreach (ToStringMode mode in Enum.GetValues(typeof(ToStringMode)))
            {
                Assert.AreEqual(identity, Identity.Parse(identity.ToString(mode)));

                Assert.IsTrue(Identity.TryParse(identity.ToString(mode), out Identity newIdentity));
                Assert.AreEqual(identity, newIdentity);
            }

            Assert.AreEqual(unicode, identity.ToString(ToStringMode.Unicode));
            if (ascii.Length > 0)
            {
                Assert.AreEqual(ascii, identity.ToString(ToStringMode.ASCII));
            }
            if (compat.Length > 0)
            {
                Assert.AreEqual(compat, identity.ToString(ToStringMode.Compat));
            }
        }
    }
}
