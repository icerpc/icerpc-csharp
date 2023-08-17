// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class IdentityTests
{
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
    [TestCase("/foo", "", "/%2Ffoo")] // cSpell:disable-line
    [TestCase("/foo", "bar", "/bar/%2Ffoo")] // cSpell:disable-line
    [TestCase("/foo", "/bar/", "/%2Fbar%2F/%2Ffoo")] // cSpell:disable-line
    [TestCase("foo/// ///#@", "/bar/", "/%2Fbar%2F/foo%2F%2F%2F%20%2F%2F%2F%23%40")] // cSpell:disable-line
    public void Convert_an_identity_to_uri_path(string name, string category, string referencePath)
    {
        var path = new Identity(name, category).ToPath();

        Assert.That(path, Is.EqualTo(referencePath));
    }

    [TestCase("/a/b/c", typeof(FormatException))]
    [TestCase("", typeof(ArgumentException))]
    [TestCase("foo", typeof(ArgumentException))]
    public void Parse_bad_path_fails(string path, Type exceptionType) =>
        Assert.That(() => Identity.Parse(path), Throws.TypeOf(exceptionType));
}
