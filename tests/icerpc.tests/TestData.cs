// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections;
using System.Collections.Generic;

namespace IceRpc.Tests
{
    /// <summary>Helper class to provide test data with <see cref="NUnit.Framework.TestCaseSourceAttribute"/>
    /// </summary>
    public abstract class TestData : IEnumerable<object[]>
    {
        private readonly List<object?[]> _data = new List<object?[]>();
        protected void AddRow(params object?[] values) => _data.Add(values);
        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class TestData<T1> : TestData
    {
        /// <summary>Adds data to the theory data set.</summary>
        /// <param name="p1">The first data value.</param>
        public void Add(T1 p1) => AddRow(p1);
    }

    public class TestData<T1, T2> : TestData
    {
        /// <summary>Adds data to the theory data set.</summary>
        /// <param name="p1">The first data value.</param>
        /// <param name="p2">The second data value.</param>
        public void Add(T1 p1, T2 p2) => AddRow(p1, p2);
    }

    public class TestData<T1, T2, T3> : TestData
    {
        /// <summary>Adds data to the theory data set.</summary>
        /// <param name="p1">The first data value.</param>
        /// <param name="p2">The second data value.</param>
        /// <param name="p3">The third data value.</param>
        public void Add(T1 p1, T2 p2, T3 p3) => AddRow(p1, p2, p3);
    }

    public class TestData<T1, T2, T3, T4> : TestData
    {
        /// <summary>Adds data to the theory data set.</summary>
        /// <param name="p1">The first data value.</param>
        /// <param name="p2">The second data value.</param>
        /// <param name="p3">The third data value.</param>
        /// <param name="p4">The fourth data value.</param>
        public void Add(T1 p1, T2 p2, T3 p3, T4 p4) => AddRow(p1, p2, p3, p4);
    }
}