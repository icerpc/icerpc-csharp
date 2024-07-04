// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(ParallelScope.All)]
public sealed partial class ExceptionTests
{
    private static IEnumerable<TestCaseData> SliceDispatchThrowsMultipleExceptionsSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12), StatusCode.ApplicationError);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.ApplicationError);
            yield return new TestCaseData(new EmptyException(), StatusCode.ApplicationError);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.ApplicationError);
        }
    }

    private static IEnumerable<TestCaseData> SliceDispatchThrowsMyExceptionSource
    {
        get
        {
            yield return new TestCaseData(new MyException(5, 12), StatusCode.ApplicationError);
            yield return new TestCaseData(new MyDerivedException(5, 12, 13, 18), StatusCode.ApplicationError);

            yield return new TestCaseData(
                new MyExceptionWithTaggedFields(5, 12, 13, 28),
                StatusCode.ApplicationError);
        }
    }

    private static IEnumerable<TestCaseData> SliceDispatchThrowsNothingSource
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

    [Test, TestCaseSource(nameof(SliceDispatchThrowsMultipleExceptionsSource))]
    public void Slice_operation_throws_exception_with_multiple_exceptions_in_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(throwException));
        var proxy = new SliceExceptionOperationsProxy(invoker);

        Type expectedType = (throwException is MyException or EmptyException) &&
            expectedStatusCode == StatusCode.ApplicationError ? throwException.GetType() : typeof(DispatchException);

        var exception = Assert.ThrowsAsync(
            expectedType,
            () => proxy.OpThrowsMultipleExceptionsAsync());

        Assert.That(exception, Is.Not.Null);
        if (expectedStatusCode != StatusCode.ApplicationError)
        {
            Assert.That(((DispatchException)exception!).StatusCode, Is.EqualTo(expectedStatusCode));
        }
    }

    [Test, TestCaseSource(nameof(SliceDispatchThrowsMyExceptionSource))]
    public void Slice_operation_throws_exception_with_my_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(throwException));
        var proxy = new SliceExceptionOperationsProxy(invoker);

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

    [Test, TestCaseSource(nameof(SliceDispatchThrowsNothingSource))]
    public void Slice_operation_throws_exception_with_no_exception_specification(
        Exception throwException,
        StatusCode expectedStatusCode)
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(throwException));
        var proxy = new SliceExceptionOperationsProxy(invoker);

        DispatchException? exception = Assert.ThrowsAsync<DispatchException>(() => proxy.OpThrowsNothingAsync());
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Test]
    public void Slice_operation_throws_invalid_data_when_exception_not_in_specification()
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(new EmptyException()));
        var proxy = new AltSliceExceptionOperationsProxy(invoker);

        InvalidDataException? exception =
            Assert.ThrowsAsync<InvalidDataException>(() => proxy.OpThrowsMultipleExceptionsAsync());
        Assert.That(exception.InnerException, Is.InstanceOf<EmptyException>());
    }

    [Test]
    public void Slice_operation_throws_invalid_data_when_operation_has_no_exception_specification()
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(new MyException(1, 2)));
        var proxy = new AltSliceExceptionOperationsProxy(invoker);

        InvalidDataException? exception =
            Assert.ThrowsAsync<InvalidDataException>(() => proxy.OpThrowsMyExceptionAsync());
        Assert.That(exception.InnerException, Is.InstanceOf<MyException>());
    }

    /// <summary>Verifies that the user can define a custom message that uses the exception fields.</summary>
    [Test]
    public void Slice_exception_can_have_custom_message()
    {
        var invoker = new ColocInvoker(new SliceExceptionOperationsService(new MyException(5, 6)));
        var proxy = new SliceExceptionOperationsProxy(invoker);

        var exception = Assert.ThrowsAsync<MyException>(() => proxy.OpThrowsMyExceptionAsync());
        Assert.That(exception.Message, Is.EqualTo("MyException: i=5, j=6"));
    }

    [SliceService]
    private sealed partial class SliceExceptionOperationsService : ISliceExceptionOperationsService
    {
        private readonly Exception _exception;

        public SliceExceptionOperationsService(Exception exception) => _exception = exception;

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
