// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(ParallelScope.All)]
public sealed partial class ExceptionTests
{
    private static IEnumerable<TestCaseData> ExceptionsInMultipleExceptionsSpecificationSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12));
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18));
            yield return new TestCaseData(new EmptyException());
        }
    }

    private static IEnumerable<TestCaseData> ExceptionsInMyExceptionSpecificationSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12));
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18));
        }
    }

    private static IEnumerable<TestCaseData> IceDispatchThrowsNothingSource
    {
        get
        {
            // The generated code does not attempt to encode any of these exceptions.

            yield return new TestCaseData(new MyException(5, 12), StatusCode.InternalError);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.InternalError);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.InternalError);
        }
    }

    [Test]
    [TestCaseSource(nameof(ExceptionsInMultipleExceptionsSpecificationSource))]
    public async Task Ice_operation_with_multiple_exceptions_throws_exception_in_specification(
        Exception throwException)
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(throwException));
        var proxy = new IceExceptionOperationsProxy(invoker);

        await Assert.ThrowsAsync(throwException.GetType(), () => proxy.OpThrowsMultipleExceptionsAsync());
    }

    [Test]
    public async Task Ice_operation_with_multiple_exceptions_throws_internal_error_for_unspecified_exception()
    {
        var invoker = new ColocInvoker(
            new IceExceptionOperationsService(new MyExceptionWithTaggedFields(5, 12, 13, 28)));
        var proxy = new IceExceptionOperationsProxy(invoker);

        DispatchException exception =
            await Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsMultipleExceptionsAsync());
        Assert.That(exception.StatusCode, Is.EqualTo(StatusCode.InternalError));
    }

    [Test]
    [TestCaseSource(nameof(ExceptionsInMyExceptionSpecificationSource))]
    public async Task Ice_operation_with_my_exception_throws_exception_in_specification(
        Exception throwException)
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(throwException));
        var proxy = new IceExceptionOperationsProxy(invoker);

        await Assert.ThrowsAsync(throwException.GetType(), () => proxy.OpThrowsMyExceptionAsync());
    }

    [Test]
    public async Task Ice_operation_with_my_exception_throws_internal_error_for_unspecified_exception()
    {
        var invoker = new ColocInvoker(
            new IceExceptionOperationsService(new MyExceptionWithTaggedFields(5, 12, 13, 28)));
        var proxy = new IceExceptionOperationsProxy(invoker);

        DispatchException exception =
            await Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsMyExceptionAsync());
        Assert.That(exception.StatusCode, Is.EqualTo(StatusCode.InternalError));
    }

    [Test]
    [TestCaseSource(nameof(IceDispatchThrowsNothingSource))]
    public async Task Ice_operation_throws_exception_with_no_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(throwException));
        var proxy = new IceExceptionOperationsProxy(invoker);

        DispatchException? exception = await Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsNothingAsync());
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Test]
    public async Task Ice_operation_throws_invalid_data_when_exception_not_in_specification()
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(new EmptyException()));
        var proxy = new AltIceExceptionOperationsProxy(invoker);

        InvalidDataException? exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => proxy.OpThrowsMultipleExceptionsAsync());
        Assert.That(exception.InnerException, Is.InstanceOf<EmptyException>());
    }

    [Test]
    public async Task Ice_operation_throws_invalid_data_when_operation_has_no_exception_specification()
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(new MyException(1, 2)));
        var proxy = new AltIceExceptionOperationsProxy(invoker);

        InvalidDataException? exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => proxy.OpThrowsMyExceptionAsync());
        Assert.That(exception.InnerException, Is.InstanceOf<MyException>());
    }

    /// <summary>Verifies that the user can define a custom message that uses the exception fields.</summary>
    [Test]
    public async Task Ice_exception_can_have_custom_message()
    {
        var invoker = new ColocInvoker(new IceExceptionOperationsService(new MyException(5, 6)));
        var proxy = new IceExceptionOperationsProxy(invoker);

        var exception = await Assert.ThrowsAsync<MyException>(() => proxy.OpThrowsMyExceptionAsync());
        Assert.That(exception.Message, Is.EqualTo("MyException: i=5, j=6"));
    }

    [Service]
    private sealed partial class IceExceptionOperationsService : IIceExceptionOperationsService
    {
        private readonly Exception _exception;

        public IceExceptionOperationsService(Exception exception) => _exception = exception;

        public ValueTask OpThrowsMultipleExceptionsAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => throw _exception;

        public ValueTask OpThrowsMyExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;

        public ValueTask OpThrowsNothingAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;
    }
}

public partial class MyException
{
    public override string Message => $"MyException: i={I}, j={J}";
}
