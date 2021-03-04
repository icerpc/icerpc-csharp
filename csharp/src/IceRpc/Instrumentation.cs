// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;

namespace IceRpc.Instrumentation
{
    /// <summary>The communicator observer interface used by the Ice run-time to obtain and update observers for its
    /// observable objects. This interface should be implemented by plug-ins that wish to observe Ice objects in order
    /// to collect statistics. An instance of this interface can be provided to the Ice run-time through the Ice
    /// communicator constructor.</summary>
    public interface ICommunicatorObserver
    {
        /// <summary>This method should return an invocation observer for the given invocation. The Ice run-time calls
        /// this method for each invocation on a proxy.</summary>
        /// <param name="prx">The proxy used for the invocation.</param>
        /// <param name="operation">The name of the invocation.</param>
        /// <param name="context">The context specified by the user.</param>
        /// <returns>The invocation observer to instrument the invocation.</returns>
        IInvocationObserver? GetInvocationObserver(
            IServicePrx prx,
            string operation,
            IReadOnlyDictionary<string, string> context);
    }

    /// <summary>The invocation observer to instrument invocations on proxies. A proxy invocation can either result in
    /// a collocated or remote invocation. If it results in a remote invocation, a sub-observer is requested for the
    /// remote invocation.</summary>
    public interface IInvocationObserver : IObserver
    {
        /// <summary>Remote exception notification.</summary>
        void RemoteException();

        /// <summary>Retry notification.</summary>
        void Retried();
    }

    /// <summary>The object observer interface used by instrumented objects to notify the observer of their existence.
    /// </summary>
    public interface IObserver
    {
        /// <summary>This method is called when the instrumented object is created or when the observer is attached to
        /// an existing object.</summary>
        void Attach();

        /// <summary>This method is called when the instrumented object is destroyed and as a result the observer
        /// detached from the object.</summary>
        void Detach();

        /// <summary>Notification of a failure.</summary>
        /// <param name="exceptionName">The name of the exception.</param>
        void Failed(string exceptionName);
    }
}
