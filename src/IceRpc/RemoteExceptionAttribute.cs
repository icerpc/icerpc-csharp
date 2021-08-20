// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to map type IDs to C# exceptions.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class RemoteExceptionAttribute : Attribute
    {
        /// <summary>The type ID assigned to the type.</summary>
        public new string TypeId => Type.GetIceTypeId()!;

        /// <summary>The type associated with this exception attribute, which designates the generated class for a
        /// Slice exception.</summary>
        public Type Type { get; }

        /// <summary>An exception delegate to create instances of <see cref="Type"/>.</summary>
        internal Func<Ice20Decoder, RemoteException> Factory
        {
            get
            {
                // The factory is lazy initialized to avoid creating a delegate each time the property is accessed
                if (_factory == null)
                {
                    ConstructorInfo? constructor = Type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(Ice20Decoder) },
                        null);

                    if (constructor == null)
                    {
                        throw new InvalidOperationException(
                            $"cannot get Ice 2.0 decoding constructor for '{Type.FullName}'");
                    }

                    ParameterExpression decoderParam = Expression.Parameter(typeof(Ice20Decoder), "decoder");

                    _factory =
                        Expression.Lambda<Func<Ice20Decoder, RemoteException>>(
                            Expression.New(constructor, decoderParam), decoderParam).Compile();
                }
                return _factory;
            }
        }

        private Func<Ice20Decoder, RemoteException>? _factory;

        /// <summary>Constructs a new instance of <see cref="RemoteExceptionAttribute"/>.</summary>
        /// <param name="type">The type of the concrete class to register.</param>
        public RemoteExceptionAttribute(Type type)
        {
            Type = type;
            Debug.Assert(typeof(RemoteException).IsAssignableFrom(type));
        }
    }
}
