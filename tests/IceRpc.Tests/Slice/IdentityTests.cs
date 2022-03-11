// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(30000)]
public class IdentityTests
{
    /// <summary>Provides test case data for
    /// <see cref="Format_identity_to_path(string name, string category, IceProxyFormat format)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> IceProxyFormatSource
    {
        get
        {
            (string, string)[] testData = {
                ("test", "\x7f€"),
                ("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a"),
                ("test", ",X2QNUAzSBcJ_e$AV;E\\"),
                ("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\"),
                ("/test", "cat/"),
            };
            foreach ((string name, string category) in testData)
            {
                foreach (IceProxyFormat format in
                    ImmutableList.Create(IceProxyFormat.Unicode, IceProxyFormat.ASCII, IceProxyFormat.Compat))
                    {
                        yield return new TestCaseData(
                            name,
                            category,
                            format);
                    }
            }
        }
    }

    /// <summary>Verifies that any arbitrary Ice Identity can represented by a URI path.</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    /// <param name="referencePath">The normalized URI path to check against.</param>
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
    public void Identity_to_path(string name, string category, string referencePath)
    {
        var path = new Identity(name, category).ToPath();

        Assert.That(path, Is.EqualTo(referencePath));
    }

    /// <summary>Verifies that Identity can be converted to an Ice string with Compat format (using ToString)
    /// and converted back to the same identity (with Parse).</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    /// <param name="compat">The "stringified" compat identity to check against (optional).</param>
    [TestCase("test", "\x7f€", "\\177\\342\\202\\254/test")]
    [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
              "greek \ud800\udd6a",
              "\"greek \\360\\220\\205\\252/banana \\016-\\360\\237\\215\\214\\342\\202\\254\\302\\242$\"")]
    public void Compat_identity_to_path(string name, string category, string compat)
    {
        var path = new Identity(name, category).ToPath();
        var proxy = new Proxy(Protocol.Ice) { Path = path };

        string identity = proxy.ToString(IceProxyFormat.Compat)[..^10];

        Assert.That(identity, Is.EqualTo(compat));
    }

    /// <summary>Verifies that Identity can be converted to an Ice string with ASCII format (using ToString)
    /// and converted back to the same identity (with Parse).</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    /// <param name="ascii">The "stringified" ascii identity to check against (optional).</param>
    [TestCase("test", "\x7f€", "\\u007f\\u20ac/test")]
    [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
              "greek \ud800\udd6a",
              "\"greek \\U0001016a/banana \\u000e-\\U0001f34c\\u20ac\\u00a2$\"")]
    public void Ascii_identity_to_path(string name, string category, string ascii)
    {
        var path = new Identity(name, category).ToPath();
        var proxy = new Proxy(Protocol.Ice) { Path = path };

        string identity = proxy.ToString(IceProxyFormat.ASCII)[..^10];

        Assert.That(identity, Is.EqualTo(ascii));
    }

    /// <summary>Verifies that Identity can be converted to an Ice string with unicode format (using ToString)
    /// and converted back to the same identity (with Parse).</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    /// <param name="unicode">The "stringified" unicode identity to check against.</param>
    [TestCase("test", "\x7f€", "\\u007f€/test")]
    [TestCase("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
              "greek \ud800\udd6a",
              "\"greek \ud800\udd6a/banana \\u000e-\ud83c\udf4c\u20ac\u00a2$\"")]
    [TestCase("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\", ",X2QNUAz\\\\SB\\\\\\/cJ_e$AV;E\\\\\\\\/test")]
    [TestCase("/test", "cat/", "cat\\//\\/test")]
    public void Unicode_identity_to_path(string name, string category, string unicode)
    {
        var path = new Identity(name, category).ToPath();
        var proxy = new Proxy(Protocol.Ice) { Path = path };

        string identity = proxy.ToString(IceProxyFormat.Unicode)[..^10]; // trim " -t -e 1.1"

        Assert.That(identity, Is.EqualTo(unicode));
    }

    /// <summary>Verifies that an Identity can be used in conjunction with an IceFormat to produce a
    /// Proxy with the correct path.</summary>
    /// <param name="name">The name field of the Identity.</param>
    /// <param name="category">The category field of the Identity.</param>
    [Test, TestCaseSource(nameof(IceProxyFormatSource))]
    public void Format_identity_to_path(string name, string category, IceProxyFormat format)
    {
        var path = new Identity(name, category).ToPath();
        var proxy = new Proxy(Protocol.Ice) { Path = path };
        string iceProxyString = proxy.ToString(format);

        var iceProxy = Proxy.Parse(iceProxyString, format: IceProxyFormat.Default);

        Assert.That(iceProxy.Path, Is.EqualTo(path));
    }
}
