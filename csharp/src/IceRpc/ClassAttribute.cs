// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Reflection;
using System.Linq.Expressions;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to map type IDs to C# classes and exceptions
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ClassAttribute : Attribute
    {
        /// <summary>The compact type ID assigned to the type or -1 if the type does not use compact type IDs.</summary>
        public int CompactTypeId { get; }

        /// <summary>The type ID assigned to the type.</summary>
        public new string TypeId { get; }

        /// <summary>The type associated to the type ID</summary>
        public Type Type { get; }

        /// <summary>A <see cref="ClassFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type does not implement <see cref="AnyClass"/>."</summary>
        internal ClassFactory? ClassFactory
        {
            get
            {
                // The delegate is lazily initialized the first time is used. This is to avoid creating delegates that are
                // never used and avoid doing all the work upfront when the attributes are loaded.
                if (_classFactory == null && typeof(AnyClass).IsAssignableFrom(Type))
                {
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(InputStream) },
                        null);
                    if (constructor == null)
                    {
                        throw new InvalidOperationException($"cannot get unmarshal constructor for '{Type.FullName}'");
                    }

                    _classFactory = (ClassFactory)Expression.Lambda(
                        typeof(ClassFactory),
                        Expression.New(constructor, Expression.Constant(null, typeof(InputStream)))).Compile();
                }
                return _classFactory;
            }
        }

        /// <summary>A <see cref="ExceptionFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type does not implement <see cref="RemoteException"/>."</summary>
        internal RemoteExceptionFactory? ExceptionFactory
        {
            get
            {
                // The delegate is lazily initialized the first time is used, this avoid creating delegates that are
                // never used and avoid doing all work upfront when the attributes are loaded
                if (_remoteExceptionFactory == null && typeof(RemoteException).IsAssignableFrom(Type))
                {
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(string), typeof(RemoteExceptionOrigin) },
                        null);

                    if (constructor == null)
                    {
                        throw new InvalidOperationException($"cannot get unmarshal constructor for '{Type.FullName}'");
                    }

                    ParameterExpression messageParam = Expression.Parameter(typeof(string), "message");
                    ParameterExpression originParam = Expression.Parameter(typeof(RemoteExceptionOrigin), "origin");

                    _remoteExceptionFactory = (RemoteExceptionFactory)Expression.Lambda(
                        typeof(RemoteExceptionFactory),
                        Expression.New(constructor, messageParam, originParam),
                        messageParam,
                        originParam).Compile();
                }
                return _remoteExceptionFactory;
            }
        }

        private ClassFactory? _classFactory;
        private RemoteExceptionFactory? _remoteExceptionFactory;

        /// <summary>Constructs a new instance of <see cref="ClassAttribute" />.</summary>
        /// <param name="typeId">The type ID.</param>
        /// <param name="compactTypeId">The compact type ID.</param>
        /// <param name="type">The type of the concrete class associated with this Ice type ID.</param>
        public ClassAttribute(string typeId, int compactTypeId, Type type)
        {
            TypeId = typeId;
            CompactTypeId = compactTypeId;
            Type = type;
            Debug.Assert(typeof(AnyClass).IsAssignableFrom(type) ||
                         (typeof(RemoteException).IsAssignableFrom(type) && compactTypeId == -1));
        }
    }
}
