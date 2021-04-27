// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.AMI
{
    public class TestIntf : IAsyncTestIntf
    {
        private readonly object _mutex = new();
        private bool _shutdown;
        private TaskCompletionSource<object?>? _pending;
        private int _value;

        public ValueTask OpAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<int> OpWithResultAsync(Dispatch dispatch, CancellationToken cancel) => new(15);

        public ValueTask OpWithUEAsync(Dispatch dispatch, CancellationToken cancel) => throw new TestIntfException();

        public ValueTask OpWithPayloadAsync(byte[] seq, Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask CloseAsync(CloseMode mode, Dispatch dispatch, CancellationToken cancel)
        {
            if (mode == CloseMode.Gracefully)
            {
                _ = dispatch.Connection.GoAwayAsync(cancel: cancel);
            }
            else
            {
                _ = dispatch.Connection.AbortAsync();
            }
            return default;
        }

        public ValueTask SleepAsync(int ms, Dispatch dispatch, CancellationToken cancel)
        {
            try
            {
                Task.Delay(ms, cancel).Wait(cancel);
                // Cancellation isn't supported with Ice1
                TestHelper.Assert(!dispatch.Context.ContainsKey("cancel") ||
                                  dispatch.Context["cancel"] == "mightSucceed" ||
                                  dispatch.Protocol == Protocol.Ice1);
            }
            catch (System.AggregateException ex) when (ex.InnerException is TaskCanceledException)
            {
                // Expected if the request is canceled.
                TestHelper.Assert(dispatch.Context.ContainsKey("cancel"));
            }
            return default;
        }

        public ValueTask ShutdownAsync(Dispatch dispatch, CancellationToken cancel)
        {
            lock (_mutex)
            {
                _shutdown = true;
                if (_pending != null)
                {
                    _pending.SetResult(null);
                    _pending = null;
                }
                _ = dispatch.Server!.ShutdownAsync();
            }
            return default;
        }

        public ValueTask<bool> SupportsAMDAsync(Dispatch dispatch, CancellationToken cancel) => new(true);

        public ValueTask<bool> SupportsFunctionalTestsAsync(Dispatch dispatch, CancellationToken cancel) => new(false);

        public async ValueTask OpAsyncDispatchAsync(Dispatch dispatch, CancellationToken cancel) =>
            await Task.Delay(10, cancel);

        public async ValueTask<int> OpWithResultAsyncDispatchAsync(Dispatch dispatch, CancellationToken cancel)
        {
            await Task.Delay(10, cancel);
            return await Self(dispatch).OpWithResultAsync(cancel: cancel);
        }

        public async ValueTask OpWithUEAsyncDispatchAsync(Dispatch dispatch, CancellationToken cancel)
        {
            await Task.Delay(10, cancel);
            try
            {
                await Self(dispatch).OpWithUEAsync(cancel: cancel);
            }
            catch (RemoteException ex)
            {
                ex.ConvertToUnhandled = false;
                throw;
            }
        }

        private static ITestIntfPrx Self(Dispatch dispatch) =>
            dispatch.Server!.CreateProxy<ITestIntfPrx>(dispatch.Path);

        public ValueTask StartDispatchAsync(Dispatch dispatch, CancellationToken cancel)
        {
            lock (_mutex)
            {
                if (_shutdown)
                {
                    // Ignore, this can occur with the forceful connection close test, shutdown can be dispatch
                    // before start dispatch.
                    var v = new TaskCompletionSource<object?>();
                    v.SetResult(null);
                    return new ValueTask(v.Task);
                }
                else if (_pending != null)
                {
                    _pending.SetResult(null);
                }
                _pending = new TaskCompletionSource<object?>();
                return new ValueTask(_pending.Task);
            }
        }

        public ValueTask FinishDispatchAsync(Dispatch dispatch, CancellationToken cancel)
        {
            lock (_mutex)
            {
                if (_shutdown)
                {
                    return default;
                }
                else if (_pending != null) // Pending might not be set yet if startDispatch is dispatch out-of-order
                {
                    _pending.SetResult(null);
                    _pending = null;
                }
            }
            return default;
        }

        public ValueTask<int> SetAsync(int newValue, Dispatch dispatch, CancellationToken cancel)
        {
            int oldValue = _value;
            _value = newValue;
            return new(oldValue);
        }

        public ValueTask SetOnewayAsync(int previousValue, int newValue, Dispatch dispatch, CancellationToken cancel)
        {
            if (_value != previousValue)
            {
                System.Console.Error.WriteLine($"previous value '{_value}' is not the expected value '{previousValue}'");
            }
            TestHelper.Assert(_value == previousValue);
            _value = newValue;
            return default;
        }
    }

    public class TestIntf2 : Outer.Inner.IAsyncTestIntf
    {
        public ValueTask<(int, int)> OpAsync(int i, Dispatch dispatch, CancellationToken cancel) => new((i, i));
    }
}
