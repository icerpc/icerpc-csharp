// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using Slice;

namespace IceRpc.Tests.Slice;

[Parallelizable(ParallelScope.All)]
public sealed class ExceptionTests
{
    private static IEnumerable<TestCaseData> Slice1DispatchThrowsAnyExceptionSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12), StatusCode.ApplicationError);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.ApplicationError);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.ApplicationError);

            // The generated code attempts to encode the exception and fails to do so since it's Slice2-only.
            yield return new TestCaseData(
                new MyExceptionWithOptionalFields(5, 12, 13, 28),
                StatusCode.UnhandledException);
        }
    }

    private static IEnumerable<TestCaseData> Slice1DispatchThrowsMyExceptionSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12), StatusCode.ApplicationError);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.ApplicationError);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.ApplicationError);

            // The generated code does not attempt to encode this exception: it becomes a run-of-the-mill unhandled.
            yield return new TestCaseData(
                new MyExceptionWithOptionalFields(5, 12, 13, 28),
                StatusCode.UnhandledException);
        }
    }

    private static IEnumerable<TestCaseData> Slice1DispatchThrowsNothingSource
    {
        get
        {
            // The generated code does not attempt to encode any of these exceptions.

            yield return new TestCaseData(new MyException(5, 12), StatusCode.UnhandledException);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.UnhandledException);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.UnhandledException);

            yield return new TestCaseData(
                new MyExceptionWithOptionalFields(5, 12, 13, 28),
                StatusCode.UnhandledException);
        }
    }

    private static IEnumerable<TestCaseData> Slice2DispatchThrowsMyExceptionSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12), StatusCode.ApplicationError);

            yield return new TestCaseData(
                new MyExceptionWithOptionalFields(5, 12, 13, 28),
                StatusCode.UnhandledException);

            // When there is an exception specification, we attempt and fail to encode the Slice1-only
            // MyDerivedException.
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.UnhandledException);
        }
    }

    private static IEnumerable<TestCaseData> Slice2DispatchThrowsNothingSource
    {
        get
        {
            // When there is no exception specification, we don't attempt to encode the Slice exception at all.

            yield return new TestCaseData(new MyException(5, 12), StatusCode.UnhandledException);

            yield return new TestCaseData(
                new MyExceptionWithOptionalFields(5, 12, 13, 28),
                StatusCode.UnhandledException);

            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.UnhandledException);
        }
    }

    [Test, TestCaseSource(nameof(Slice2DispatchThrowsMyExceptionSource))]
    public void Slice2_dispatch_throws_exception_with_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        // Arrange
        var invoker = new ColocInvoker(new Slice2ExceptionOperationsService(throwException));
        var proxy = new Slice2ExceptionOperationsProxy(invoker);

        Type expectedType = throwException is MyException && expectedStatusCode == StatusCode.ApplicationError ?
            throwException.GetType() : typeof(DispatchException);

        // Act/Assert
        var exception = Assert.ThrowsAsync(
                expectedType,
                () => proxy.OpThrowsMyExceptionAsync());

        Assert.That(exception, Is.Not.Null);
        if (expectedStatusCode != StatusCode.ApplicationError)
        {
            Assert.That(((DispatchException)exception!).StatusCode, Is.EqualTo(expectedStatusCode));
        }
    }

    [Test, TestCaseSource(nameof(Slice2DispatchThrowsNothingSource))]
    public void Slice2_dispatch_throws_exception_without_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        // Arrange
        var invoker = new ColocInvoker(new Slice2ExceptionOperationsService(throwException));
        var proxy = new Slice2ExceptionOperationsProxy(invoker);

        // Act/Assert
        DispatchException? exception = Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsNothingAsync());
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Test, TestCaseSource(nameof(Slice1DispatchThrowsAnyExceptionSource))]
    public void Slice1_operation_throws_exception_with_any_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new Slice1ExceptionOperationsService(throwException));
        var proxy = new Slice1ExceptionOperationsProxy(invoker);

        Type expectedType = expectedStatusCode == StatusCode.ApplicationError ?
            throwException.GetType() : typeof(DispatchException);

        var exception = Assert.ThrowsAsync(
            expectedType,
            () => proxy.OpThrowsAnyExceptionAsync());

        Assert.That(exception, Is.Not.Null);
        if (expectedStatusCode != StatusCode.ApplicationError)
        {
            Assert.That(((DispatchException)exception!).StatusCode, Is.EqualTo(expectedStatusCode));
        }
    }

    [Test, TestCaseSource(nameof(Slice1DispatchThrowsMyExceptionSource))]
    public void Slice1_operation_throws_exception_with_my_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new Slice1ExceptionOperationsService(throwException));
        var proxy = new Slice1ExceptionOperationsProxy(invoker);

        Type expectedType = throwException is MyException && expectedStatusCode == StatusCode.ApplicationError ?
            throwException.GetType() : typeof(DispatchException);

        var exception = Assert.ThrowsAsync(
                expectedType,
                () => proxy.OpThrowsMyExceptionAsync());

        Assert.That(exception, Is.Not.Null);
        if (expectedStatusCode != StatusCode.ApplicationError)
        {
            Assert.That(((DispatchException)exception!).StatusCode, Is.EqualTo(expectedStatusCode));
        }
    }

    [Test, TestCaseSource(nameof(Slice1DispatchThrowsNothingSource))]
    public void Slice1_operation_throws_exception_with_no_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new Slice1ExceptionOperationsService(throwException));
        var proxy = new Slice1ExceptionOperationsProxy(invoker);

        DispatchException? exception = Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsNothingAsync());
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    private sealed class Slice2ExceptionOperationsService : Service, ISlice2ExceptionOperationsService
    {
        private readonly Exception _exception;

        public Slice2ExceptionOperationsService(Exception exception) => _exception = exception;

        public ValueTask OpThrowsMyExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;

        public ValueTask OpThrowsNothingAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;

        public ValueTask<MyExceptionWithOptionalFields> OpExceptionParamAsync(
            MyExceptionWithOptionalFields exception,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(exception);
    }

    private sealed class Slice1ExceptionOperationsService : Service, ISlice1ExceptionOperationsService
    {
        private readonly Exception _exception;

        public Slice1ExceptionOperationsService(Exception exception) => _exception = exception;

        public ValueTask OpThrowsAnyExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;

        public ValueTask OpThrowsMyExceptionAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;

        public ValueTask OpThrowsNothingAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw _exception;
    }
}
