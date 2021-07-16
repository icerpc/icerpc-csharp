// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to map type IDs to C# classes and exceptions
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ClassAttribute : Attribute
    {
        /// <summary>The compact type ID assigned to the type or null if the type does not have a compact type ID.
        /// </summary>
        public int? CompactTypeId => Type.GetIceCompactTypeId();

        /// <summary>The type ID assigned to the type.</summary>
        public new string TypeId
        {
            get
            {
                string? typeId = Type.GetIceTypeId();
                // Using the ClassAttribute with a type without associated TypeId indicate a bug in the generated code.
                Debug.Assert(typeId != null);
                return typeId;
            }
        }

        /// <summary>The class type associated with this class attribute, which designates the generated class for a
        /// Slice class or exception.</summary>
        public Type Type { get; }

        /// <summary>A <see cref="ClassFactory"/> delegate to create instances of <see cref="Type"/>.</summary>
        internal Func<AnyClass> ClassFactory
        {
            get
            {
                // The factory is lazy initialize to avoid creating a delegate each time the property is accessed
                if (_classFactory == null)
                {
                    Debug.Assert(typeof(AnyClass).IsAssignableFrom(Type));
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(IceDecoder) },
                        null);
                    if (constructor == null)
                    {
                        throw new InvalidOperationException($"cannot get unmarshal constructor for '{Type.FullName}'");
                    }

                    _classFactory = Expression.Lambda<Func<AnyClass>>(
                        Expression.New(constructor, Expression.Constant(null, typeof(IceDecoder)))).Compile();
                }
                return _classFactory;
            }
        }

        /// <summary>A <see cref="ExceptionFactory"/> delegate to create instances of <see cref="Type"/>.</summary>
        internal Func<string?, RemoteExceptionOrigin, RemoteException> ExceptionFactory
        {
            get
            {
                // The factory is lazy initialize to avoid creating a delegate each time the property is accessed
                if (_exceptionFactory == null)
                {
                    Debug.Assert(typeof(RemoteException).IsAssignableFrom(Type));

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

                    _exceptionFactory = Expression.Lambda<Func<string?, RemoteExceptionOrigin, RemoteException>>(
                        Expression.New(constructor, messageParam, originParam),
                        messageParam,
                        originParam).Compile();
                }
                return _exceptionFactory;
            }
        }

        private Func<AnyClass>? _classFactory;
        private Func<string?, RemoteExceptionOrigin, RemoteException>? _exceptionFactory;

        /// <summary>Constructs a new instance of <see cref="ClassAttribute" />.</summary>
        /// <param name="type">The type of the concrete class to register.</param>
        public ClassAttribute(Type type)
        {
            Type = type;
            Debug.Assert(typeof(AnyClass).IsAssignableFrom(type) || typeof(RemoteException).IsAssignableFrom(type));
        }
    }
}
