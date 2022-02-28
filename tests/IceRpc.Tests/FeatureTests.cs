// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Timeout(30000)]
public class FeatureCollectionTests
{
    [Test]
    public void FeatureCollection_Empty()
    {
        Assert.That(FeatureCollection.Empty, Is.Empty);
        Assert.That(FeatureCollection.Empty.IsReadOnly, Is.True);
        Assert.Throws<InvalidOperationException>(() => FeatureCollection.Empty.Set("foo"));
    }

    [Test]
    public void FeatureCollection_GetSet()
    {
        var features = new FeatureCollection();

        Assert.That(features.Get<string>(), Is.Null);
        features.Set("foo");
        Assert.That(features.Get<string>(), Is.EqualTo("foo"));

        // Test defaults
        var features2 = new FeatureCollection(features);

        Assert.That(features2.Get<string>(), Is.EqualTo("foo"));
        features2.Set("bar");
        Assert.That(features.Get<string>(), Is.EqualTo("foo"));
        Assert.That(features2.Get<string>(), Is.EqualTo("bar"));

        features2.Set<string>(null);
        Assert.That(features.Get<string>(), Is.EqualTo("foo"));
        Assert.That(features2.Get<string>(), Is.EqualTo("foo"));
    }

    [Test]
    public void FeatureCollection_Index()
    {
        var features = new FeatureCollection();

        Assert.That(features[typeof(int)], Is.Null);

        features[typeof(int)] = 42;
        Assert.That((int)features[typeof(int)]!, Is.EqualTo(42));
    }
}
