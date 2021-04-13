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
        public int? CompactTypeId => Type.GetIceCompactTypeId();

        /// <summary>The type ID assigned to the type.</summary>
        public new string? TypeId => Type.GetIceTypeId();

        /// <summary>The class type associated with this class attribute, which designates the generated class for a
        /// Slice class or exception.</summary>
        public Type Type { get; }

        /// <summary>A <see cref="ClassFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type does not implement <see cref="AnyClass"/>."</summary>
        internal ClassFactory? ClassFactory
        {
            get
            {
                if (typeof(AnyClass).IsAssignableFrom(Type))
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

                    return (ClassFactory)Expression.Lambda(
                        typeof(ClassFactory),
                        Expression.New(constructor, Expression.Constant(null, typeof(InputStream)))).Compile();
                }
                return null;
            }
        }

        /// <summary>A <see cref="ExceptionFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type does not implement <see cref="RemoteException"/>."</summary>
        internal RemoteExceptionFactory? ExceptionFactory
        {
            get
            {
                if (typeof(RemoteException).IsAssignableFrom(Type))
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

                    return (RemoteExceptionFactory)Expression.Lambda(
                        typeof(RemoteExceptionFactory),
                        Expression.New(constructor, messageParam, originParam),
                        messageParam,
                        originParam).Compile();
                }
                return null;
            }
        }

        /// <summary>Constructs a new instance of <see cref="ClassAttribute" />.</summary>
        /// <param name="type">The type of the concrete class to register.</param>
        public ClassAttribute(Type type)
        {
            Type = type;
            Debug.Assert(typeof(AnyClass).IsAssignableFrom(type) || typeof(RemoteException).IsAssignableFrom(type));
        }
    }
}
