// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class EncodedResultTests
{
    /// <summary>Verifies that an interface annotated with [cs:encoded-result] attribute uses the generated
    /// encoded result struct for its operations return types.</summary>
    [Test]
    public void Encoded_result_interface_attribute()
    {
        // Arrange
        MethodInfo? method = typeof(IEncodedResultOperations1).GetMethod("OpStringAsync");

        // Act

        // Assert
        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(ValueTask<IEncodedResultOperations1.OpStringEncodedResult>)));
    }

    /// <summary>Verifies that an operation annotated with [cs:encoded-result] attribute uses the generated
    /// encoded result struct for its return type.</summary>
    [Test]
    public void Encoded_result_operation_attribute()
    {
        // Arrange
        MethodInfo? method = typeof(IEncodedResultOperations2).GetMethod("OpStringAsync");

        // Act

        // Assert
        Assert.That(method, Is.Not.Null);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(ValueTask<IEncodedResultOperations2.OpStringEncodedResult>)));
    }
}
