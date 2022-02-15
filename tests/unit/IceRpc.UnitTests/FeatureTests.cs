// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Multiplier = System.Int32;

namespace IceRpc.UnitTests;

[Timeout(30000)]
public class FeatureCollectionTests
{
    [Test]
    public void FeatureCollection_GetSet()
    {
        var features = new FeatureCollection();

        Assert.That(features.Get<string>(), Is.Null);

        features.Set("foo");
        string? s = features.Get<string>();
        Assert.That(s, Is.Not.Null);
        Assert.AreEqual("foo", s!);

        // Test defaults
        var features2 = new FeatureCollection(features);

        Assert.AreEqual("foo", features2.Get<string>());
        features2.Set("bar");
        Assert.AreEqual("foo", features.Get<string>());
        Assert.AreEqual("bar", features2.Get<string>());

        features2.Set<string>(null);
        Assert.AreEqual("foo", features.Get<string>());
        Assert.AreEqual("foo", features2.Get<string>());
    }

    [Test]
    public void FeatureCollection_Index()
    {
        var features = new FeatureCollection();

        Assert.That(features[typeof(int)], Is.Null);

        features[typeof(int)] = 42;
        Assert.AreEqual(42, (int)features[typeof(int)]!);
    }
}
