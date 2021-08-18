// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Globalization;
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
        public string? CompactTypeId => Type.GetIceCompactTypeId()?.ToString(CultureInfo.InvariantCulture);

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

        /// <summary>A factory delegate to create instances of <see cref="Type"/>.</summary>
        internal Func<object> Factory
        {
            get
            {
                // The factory is lazy initialize to avoid creating a delegate each time the property is accessed
                if (_factory == null)
                {
                    Debug.Assert(typeof(AnyClass).IsAssignableFrom(Type) ||
                                 typeof(RemoteException).IsAssignableFrom(Type));
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(Ice11Decoder) },
                        null);
                    if (constructor == null)
                    {
                        throw new InvalidOperationException($"cannot get 1.1 constructor for '{Type.FullName}'");
                    }

                    _factory = Expression.Lambda<Func<object>>(
                        Expression.New(constructor, Expression.Constant(null, typeof(Ice11Decoder)))).Compile();
                }
                return _factory;
            }
        }

        /// <summary>An exception delegate to create instances of <see cref="Type"/>.</summary>
        internal Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException> ExceptionFactory20
        {
            get
            {
                // The factory is lazy initialize to avoid creating a delegate each time the property is accessed
                if (_exceptionFactory20 == null)
                {
                    Debug.Assert(typeof(RemoteException).IsAssignableFrom(Type));

                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(string), typeof(RemoteExceptionOrigin), typeof(Ice20Decoder) },
                        null);

                    if (constructor == null)
                    {
                        throw new InvalidOperationException($"cannot get 2.0 constructor for '{Type.FullName}'");
                    }

                    ParameterExpression messageParam = Expression.Parameter(typeof(string), "message");
                    ParameterExpression originParam = Expression.Parameter(typeof(RemoteExceptionOrigin), "origin");
                    ParameterExpression decoderParam = Expression.Parameter(typeof(Ice20Decoder), "decoder");

                    _exceptionFactory20 =
                        Expression.Lambda<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>(
                            Expression.New(constructor, messageParam, originParam, decoderParam),
                            messageParam,
                            originParam,
                            decoderParam).Compile();
                }
                return _exceptionFactory20;
            }
        }

        private Func<object>? _factory;

        private Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>? _exceptionFactory20;

        /// <summary>Constructs a new instance of <see cref="ClassAttribute" />.</summary>
        /// <param name="type">The type of the concrete class to register.</param>
        public ClassAttribute(Type type)
        {
            Type = type;
            Debug.Assert(typeof(AnyClass).IsAssignableFrom(type) || typeof(RemoteException).IsAssignableFrom(type));
        }
    }
}
