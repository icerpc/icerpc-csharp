// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Reflection;
using System.Linq.Expressions;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to map type IDs to C# classes
    /// and exceptions.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ClassAttribute : Attribute
    {
        /// <summary>The compact type ID assigned to the type or 0 if the type does not use compact type IDs.</summary>
        public int CompactTypeID { get; }
        /// <summary>The type ID assigned to the type.</summary>
        public string TypeID { get; }
        /// <summary>The type associated to the type ID</summary>
        public Type Type { get; }

        /// <summary>A <see cref="ClassFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type doesn't implements <see cref="AnyClass"/>."</summary>
        public ClassFactory? ClassFactory
        {
            get
            {
                if (typeof(AnyClass).IsAssignableFrom(Type) && _classFactory == null)
                {
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(InputStream) },
                        null);
                    
                    if (constructor == null)
                    {
                        Debug.Assert(false);
                        return null; // TODO throw InvalidMethodException and make this property non nullable?
                    }

                    _classFactory = (ClassFactory)Expression.Lambda(
                        typeof(ClassFactory),
                        Expression.New(constructor, Expression.Constant(null, typeof(InputStream)))).Compile();
                }
                return _classFactory;
            }
        }

        /// <summary>A <see cref="ClassFactory"/> delegate to create instances of <see cref="Type"/> or null if the
        /// type doesn't implements <see cref="RemoteException"/>."</summary>
        public RemoteExceptionFactory? ExceptionFactory
        {
            get
            {
                if (typeof(RemoteException).IsAssignableFrom(Type) && _exceptionFactory == null)
                {
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(string), typeof(RemoteExceptionOrigin) },
                        null);

                    if (constructor == null)
                    {
                        Debug.Assert(false);
                        return null; // TODO throw InvalidMethodException and make this property non nullable?
                    }

                    ParameterExpression messageParam = Expression.Parameter(typeof(string), "message");
                    ParameterExpression originParam = Expression.Parameter(typeof(RemoteExceptionOrigin), "origin");

                    _exceptionFactory = (RemoteExceptionFactory)Expression.Lambda(
                        typeof(RemoteExceptionFactory),
                        Expression.New(constructor, messageParam, originParam),
                        messageParam,
                        originParam).Compile();
                }
                return _exceptionFactory;
            }
        }

        private ClassFactory? _classFactory;
        private RemoteExceptionFactory? _exceptionFactory;

        /// <summary>Constructs a new instance of <see cref="ClassAttribute" />.</summary>
        /// <param name="typeID">The type ID.</param>
        /// <param name="compactTypeID">The compact type ID.</param>
        /// <param name="type">The type to associate with the compact type ID.</param>
        public ClassAttribute(string typeID, int compactTypeID, Type type)
        {
            TypeID = typeID;
            CompactTypeID = compactTypeID;
            Type = type;
            Debug.Assert(typeof(AnyClass).IsAssignableFrom(type) || 
                         (typeof(RemoteException).IsAssignableFrom(type) && compactTypeID == 0));
        }
    }
}
