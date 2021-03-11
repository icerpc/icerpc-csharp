// Copyright (c) ZeroC, Inc. All rights reserved.

// Ice version 4.0.0-alpha.0
//
// Generated from file `Namespace.ice'
//
// Warning: do not edit this file.
//

#nullable enable
#pragma warning disable SA1300 // Element must begin with upper case letter
#pragma warning disable SA1306 // Field names must begin with lower case letter
#pragma warning disable SA1309 // Field names must not begin with underscore
#pragma warning disable SA1312 // Variable names must begin with lower case letter
#pragma warning disable SA1313 // Parameter names must begin with lower case letter
#pragma warning disable CA1033 // Interface methods should be callable by child types
#pragma warning disable CA1707 // Remove the underscores from member name
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix

#pragma warning disable 1591
namespace IceRpc.Test.NamespaceMD.WithNamespace
{
    namespace N1.N2
    {
        public partial struct S1 : global::System.IEquatable<S1>, IceRpc.IStreamableStruct
        {
            /// <summary>A <see cref="IceRpc.InputStreamReader{T}"/> used to read <see cref="S1"/> instances.</summary>
            public static readonly IceRpc.InputStreamReader<S1> IceReader =
                istr => new S1(istr);

            /// <summary>A <see cref="IceRpc.OutputStreamWriter{T}"/> used to write <see cref="S1"/> instances.</summary>
            public static readonly IceRpc.OutputStreamWriter<S1> IceWriter =
                (ostr, value) => value.IceWrite(ostr);

            public int I;

            /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
            /// <param name="lhs">The left hand side operand.</param>
            /// <param name="rhs">The right hand side operand.</param>
            /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
            public static bool operator ==(S1 lhs, S1 rhs) => lhs.Equals(rhs);

            /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
            /// <param name="lhs">The left hand side operand.</param>
            /// <param name="rhs">The right hand side operand.</param>
            /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
            public static bool operator !=(S1 lhs, S1 rhs) => !lhs.Equals(rhs);

            /// <summary>Constructs a new instance of <see cref="S1"/>.</summary>
            public S1(int i)
            {
                I = i;
                Initialize();
            }

            /// <summary>Constructs a new instance of <see cref="S1"/>.</summary>
            /// <param name="istr">The <see cref="IceRpc.InputStream"/> being used to unmarshal the instance.</param>
            public S1(IceRpc.InputStream istr)
            {
                this.I = istr.ReadInt();
                Initialize();
            }

            /// <inheritdoc/>
            public readonly override bool Equals(object? obj) => obj is S1 value && this.Equals(value);

            /// <inheritdoc/>
            public readonly bool Equals(S1 other) =>
                this.I == other.I;

            /// <inheritdoc/>
            public readonly override int GetHashCode()
            {
                var hash = new global::System.HashCode();
                hash.Add(this.I);
                return hash.ToHashCode();
            }

            /// <summary>Marshals the struct by writing its fields to the <see cref="IceRpc.OutputStream"/>.</summary>
            /// <param name="ostr">The stream to write to.</param>
            public readonly void IceWrite(IceRpc.OutputStream ostr)
            {
                ostr.WriteInt(this.I);
            }

            /// <summary>The constructor calls the Initialize partial method after initializing the fields.</summary>
            partial void Initialize();
        }
    }

    [IceRpc.TypeId("::WithNamespace::C1")]
    public partial class C1 : IceRpc.AnyClass
    {
        public int I;

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.InputStreamReader<C1> IceReader =
            istr => istr.ReadClass<C1>(IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.InputStreamReader<C1?> IceReaderIntoNullable =
            istr => istr.ReadNullableClass<C1>(IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static string IceTypeId => _iceAllTypeIds[0];

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.OutputStreamWriter<C1> IceWriter =
            (ostr, value) => ostr.WriteClass(value, IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.OutputStreamWriter<C1?> IceWriterFromNullable =
            (ostr, value) => ostr.WriteNullableClass(value, IceTypeId);

        private static readonly string[] _iceAllTypeIds = IceRpc.TypeExtensions.GetAllIceTypeIds(typeof(C1));

        partial void Initialize();

        /// <summary>Constructs a new instance of <see cref="C1"/>.</summary>
        public C1(int i)
        {
            this.I = i;
            Initialize();
        }

        /// <summary>Constructs a new instance of <see cref="C1"/>.</summary>
        public C1()
        {
            Initialize();
        }

        [global::System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Performance",
            "CA1801: Review unused parameters",
            Justification="Special constructor used for Ice unmarshaling")]
        protected internal C1(IceRpc.InputStream? istr)
        {
        }

        protected override void IceWrite(IceRpc.OutputStream ostr, bool firstSlice)
        {
            if (firstSlice)
            {
                ostr.IceStartFirstSlice(_iceAllTypeIds);
            }
            else
            {
                ostr.IceStartNextSlice(IceTypeId);
            }
            ostr.WriteInt(this.I);
            ostr.IceEndSlice(true);
        }

        protected override void IceRead(IceRpc.InputStream istr, bool firstSlice)
        {
            if (firstSlice)
            {
                _ = istr.IceStartFirstSlice();
            }
            else
            {
                istr.IceStartNextSlice();
            }
            this.I = istr.ReadInt();
            istr.IceEndSlice();
            Initialize();
        }
    }

    [IceRpc.TypeId("::WithNamespace::C2")]
    public partial class C2 : C1
    {
        public long L;

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.InputStreamReader<C2> IceReader =
            istr => istr.ReadClass<C2>(IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.InputStreamReader<C2?> IceReaderIntoNullable =
            istr => istr.ReadNullableClass<C2>(IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static new string IceTypeId => _iceAllTypeIds[0];

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.OutputStreamWriter<C2> IceWriter =
            (ostr, value) => ostr.WriteClass(value, IceTypeId);

        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
        public static readonly new IceRpc.OutputStreamWriter<C2?> IceWriterFromNullable =
            (ostr, value) => ostr.WriteNullableClass(value, IceTypeId);

        private static readonly string[] _iceAllTypeIds = IceRpc.TypeExtensions.GetAllIceTypeIds(typeof(C2));

        partial void Initialize();

        /// <summary>Constructs a new instance of <see cref="C2"/>.</summary>
        public C2(int i, long l)
            : base(i)
        {
            this.L = l;
            Initialize();
        }

        /// <summary>Constructs a new instance of <see cref="C2"/>.</summary>
        public C2()
        {
            Initialize();
        }

        protected internal C2(IceRpc.InputStream? istr)
            : base(istr)
        {
        }

        protected override void IceWrite(IceRpc.OutputStream ostr, bool firstSlice)
        {
            if (firstSlice)
            {
                ostr.IceStartFirstSlice(_iceAllTypeIds);
            }
            else
            {
                ostr.IceStartNextSlice(IceTypeId);
            }
            ostr.WriteLong(this.L);
            ostr.IceEndSlice(false);
            base.IceWrite(ostr, false);
        }

        protected override void IceRead(IceRpc.InputStream istr, bool firstSlice)
        {
            if (firstSlice)
            {
                _ = istr.IceStartFirstSlice();
            }
            else
            {
                istr.IceStartNextSlice();
            }
            this.L = istr.ReadLong();
            istr.IceEndSlice();
            base.IceRead(istr, false);
            Initialize();
        }
    }

    [IceRpc.TypeId("::WithNamespace::E1")]
    public partial class E1 : IceRpc.RemoteException
    {
        public int I;
        private readonly string[] _iceAllTypeIds = IceRpc.TypeExtensions.GetAllIceTypeIds(typeof(E1));

        /// <summary>Constructs a new instance of <see cref="E1"/>.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E1(int i, IceRpc.RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
            this.I = i;
        }

        /// <summary>Constructs a new instance of <see cref="E1"/>.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E1(string? message, int i, global::System.Exception? innerException = null, IceRpc.RetryPolicy retryPolicy = default)
            : base(message, innerException, retryPolicy)
        {
            this.I = i;
        }

        /// <summary>Constructs a new instance of <see cref="E1"/>.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E1(IceRpc.RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }

        protected internal E1(string? message, IceRpc.RemoteExceptionOrigin origin)
            : base(message, origin)
        {
        }

        protected override void IceRead(IceRpc.InputStream istr, bool firstSlice)
        {
            if (firstSlice)
            {
                IceSlicedData = istr.IceStartFirstSlice();
                ConvertToUnhandled = true;
            }
            else
            {
                istr.IceStartNextSlice();
            }
            this.I = istr.ReadInt();
            istr.IceEndSlice();
        }

        protected override void IceWrite(IceRpc.OutputStream ostr, bool firstSlice)
        {
            if (firstSlice)
            {
                ostr.IceStartFirstSlice(_iceAllTypeIds, IceSlicedData, errorMessage: Message, origin: Origin);
            }
            else
            {
                ostr.IceStartNextSlice(_iceAllTypeIds[0]);
            }
            ostr.WriteInt(this.I);
            ostr.IceEndSlice(true);
        }
    }

    [IceRpc.TypeId("::WithNamespace::E2")]
    public partial class E2 : E1
    {
        public long L;
        private readonly string[] _iceAllTypeIds = IceRpc.TypeExtensions.GetAllIceTypeIds(typeof(E2));

        /// <summary>Constructs a new instance of <see cref="E2"/>.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E2(int i, long l, IceRpc.RetryPolicy retryPolicy = default)
            : base(i, retryPolicy)
        {
            this.L = l;
        }

        /// <summary>Constructs a new instance of <see cref="E2"/>.</summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E2(string? message, int i, long l, global::System.Exception? innerException = null, IceRpc.RetryPolicy retryPolicy = default)
            : base(message, i, innerException, retryPolicy)
        {
            this.L = l;
        }

        /// <summary>Constructs a new instance of <see cref="E2"/>.</summary>
        /// <param name="retryPolicy">The retry policy for the exception.</param>
        public E2(IceRpc.RetryPolicy retryPolicy = default)
            : base(retryPolicy)
        {
        }

        protected internal E2(string? message, IceRpc.RemoteExceptionOrigin origin)
            : base(message, origin)
        {
        }

        protected override void IceRead(IceRpc.InputStream istr, bool firstSlice)
        {
            if (firstSlice)
            {
                IceSlicedData = istr.IceStartFirstSlice();
                ConvertToUnhandled = true;
            }
            else
            {
                istr.IceStartNextSlice();
            }
            this.L = istr.ReadLong();
            istr.IceEndSlice();
            base.IceRead(istr, false);
        }

        protected override void IceWrite(IceRpc.OutputStream ostr, bool firstSlice)
        {
            if (firstSlice)
            {
                ostr.IceStartFirstSlice(_iceAllTypeIds, IceSlicedData, errorMessage: Message, origin: Origin);
            }
            else
            {
                ostr.IceStartNextSlice(_iceAllTypeIds[0]);
            }
            ostr.WriteLong(this.L);
            ostr.IceEndSlice(false);
            base.IceWrite(ostr, false);
        }
    }
}
namespace IceRpc.Test.NamespaceMD.M1.M2.M3
{
    public partial struct S1 : global::System.IEquatable<S1>, IceRpc.IStreamableStruct
    {
        /// <summary>A <see cref="IceRpc.InputStreamReader{T}"/> used to read <see cref="S1"/> instances.</summary>
        public static readonly IceRpc.InputStreamReader<S1> IceReader =
            istr => new S1(istr);

        /// <summary>A <see cref="IceRpc.OutputStreamWriter{T}"/> used to write <see cref="S1"/> instances.</summary>
        public static readonly IceRpc.OutputStreamWriter<S1> IceWriter =
            (ostr, value) => value.IceWrite(ostr);

        public int I;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(S1 lhs, S1 rhs) => lhs.Equals(rhs);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(S1 lhs, S1 rhs) => !lhs.Equals(rhs);

        /// <summary>Constructs a new instance of <see cref="S1"/>.</summary>
        public S1(int i)
        {
            I = i;
            Initialize();
        }

        /// <summary>Constructs a new instance of <see cref="S1"/>.</summary>
        /// <param name="istr">The <see cref="IceRpc.InputStream"/> being used to unmarshal the instance.</param>
        public S1(IceRpc.InputStream istr)
        {
            this.I = istr.ReadInt();
            Initialize();
        }

        /// <inheritdoc/>
        public readonly override bool Equals(object? obj) => obj is S1 value && this.Equals(value);

        /// <inheritdoc/>
        public readonly bool Equals(S1 other) =>
            this.I == other.I;

        /// <inheritdoc/>
        public readonly override int GetHashCode()
        {
            var hash = new global::System.HashCode();
            hash.Add(this.I);
            return hash.ToHashCode();
        }

        /// <summary>Marshals the struct by writing its fields to the <see cref="IceRpc.OutputStream"/>.</summary>
        /// <param name="ostr">The stream to write to.</param>
        public readonly void IceWrite(IceRpc.OutputStream ostr)
        {
            ostr.WriteInt(this.I);
        }

        /// <summary>The constructor calls the Initialize partial method after initializing the fields.</summary>
        partial void Initialize();
    }
}
namespace IceRpc.ClassFactoryWithNamespace
{
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public static class C1
    {
        public static global::IceRpc.AnyClass Create() =>
            new global::IceRpc.Test.NamespaceMD.WithNamespace.C1((global::IceRpc.InputStream?)null);
    }

    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public static class C2
    {
        public static global::IceRpc.AnyClass Create() =>
            new global::IceRpc.Test.NamespaceMD.WithNamespace.C2((global::IceRpc.InputStream?)null);
    }
}
namespace IceRpc.RemoteExceptionFactoryWithNamespace
{
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public static class E1
    {
        public static global::IceRpc.RemoteException Create(string? message, global::IceRpc.RemoteExceptionOrigin origin) =>
            new global::IceRpc.Test.NamespaceMD.WithNamespace.E1(message, origin);
    }

    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public static class E2
    {
        public static global::IceRpc.RemoteException Create(string? message, global::IceRpc.RemoteExceptionOrigin origin) =>
            new global::IceRpc.Test.NamespaceMD.WithNamespace.E2(message, origin);
    }
}
