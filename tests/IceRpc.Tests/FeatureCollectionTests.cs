// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using NUnit.Framework;

namespace IceRpc.Tests;

public class FeatureCollectionTests
{
    /// <summary>Verifies that we can get the value of a feature that is set in the defaults.</summary>
    [Test]
    public void Getting_a_feature_from_defaults()
    {
        IFeatureCollection features = new FeatureCollection();
        features.Set("foo");
        IFeatureCollection features2 = new FeatureCollection(features);

        string? feature = features2.Get<string>();

        Assert.That(feature, Is.EqualTo("foo"));
    }

    /// <summary>Verifies that get returns the <c>default</c> for an unset feature.</summary>
    [Test]
    public void Getting_an_unset_feature_returns_the_default()
    {
        IFeatureCollection features = new FeatureCollection();

        int feature = features.Get<int>();

        Assert.That(feature, Is.EqualTo(0));
    }

    /// <summary>Verifies that we can set a feature.</summary>
    [Test]
    public void Setting_a_feature()
    {
        IFeatureCollection features = new FeatureCollection();

        features.Set("foo");

        Assert.That(features.Get<string>(), Is.EqualTo("foo"));
    }

    /// <summary>Verifies that setting a feature overwrites the value set in the defaults.</summary>
    [Test]
    public void Setting_a_feature_overwrites_the_default_value()
    {
        IFeatureCollection features = new FeatureCollection();
        features.Set("foo");
        IFeatureCollection features2 = new FeatureCollection(features);

        features2.Set("bar");

        Assert.That(features2.Get<string>(), Is.EqualTo("bar"));
        Assert.That(features.Get<string>(), Is.EqualTo("foo"));
    }

    /// <summary>Verifies that setting a feature to null removes the feature.</summary>
    [Test]
    public void Setting_a_feature_to_null_removes_the_feature()
    {
        IFeatureCollection features = new FeatureCollection();
        features.Set("foo");

        features.Set<string>(null);

        Assert.That(features.Any(), Is.False);
    }

    /// <summary>Verifies that we can set a feature using the index operator.</summary>
    [Test]
    public void Setting_a_feature_using_index_operator()
    {
        IFeatureCollection features = new FeatureCollection();

        features[typeof(int)] = 42;

        Assert.That((int)features[typeof(int)]!, Is.EqualTo(42));
    }

    [Test]
    public void Iterate_returns_all_features_except_masked_defaults()
    {
        // Arrange
        IFeatureCollection f1 = new FeatureCollection
        {
            [typeof(int)] = 1,
            [typeof(long)] = 128,
        };

        IFeatureCollection f2 = new FeatureCollection(f1)
        {
            [typeof(long)] = 256, // Mask default
        };

        IFeatureCollection f3 = new FeatureCollection(f2);

        IFeatureCollection f4 = new FeatureCollection(f3)
        {
            [typeof(int)] = 2, // Mask the default
            [typeof(string)] = "hello"
        };

        // Act
        var all = f4.ToDictionary(x => x.Key, x => x.Value);

        // Assert
        var expected = new Dictionary<Type, object>
        {
            [typeof(int)] = 2,
            [typeof(string)] = "hello",
            [typeof(long)] = 256
        };
        Assert.That(all, Is.EqualTo(expected));
    }
}
