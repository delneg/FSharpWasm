// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace System
{
    public static partial class TupleExtensions
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1>(this Tuple<T1> value, out T1 item1)
        {
            item1 = value.Item1;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2>(this Tuple<T1, T2> value, out T1 item1, out T2 item2)
        {
            item1 = value.Item1;
            item2 = value.Item2;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2, T3>(this Tuple<T1, T2, T3> value, out T1 item1, out T2 item2, out T3 item3)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
            item4 = value.Item4;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
            item4 = value.Item4;
            item5 = value.Item5;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
            item4 = value.Item4;
            item5 = value.Item5;
            item6 = value.Item6;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Deconstruct<T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> value, out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6, out T7 item7)
        {
            item1 = value.Item1;
            item2 = value.Item2;
            item3 = value.Item3;
            item4 = value.Item4;
            item5 = value.Item5;
            item6 = value.Item6;
            item7 = value.Item7;
        }

        public static ValueTuple<T1> ToValueTuple<T1>(this Tuple<T1> value) =>
            ValueTuple.Create(value.Item1);

        public static ValueTuple<T1, T2> ToValueTuple<T1, T2>(this Tuple<T1, T2> value) =>
            ValueTuple.Create(value.Item1, value.Item2);

        public static ValueTuple<T1, T2, T3> ToValueTuple<T1, T2, T3>(this Tuple<T1, T2, T3> value) =>
            ValueTuple.Create(value.Item1, value.Item2, value.Item3);

        public static ValueTuple<T1, T2, T3, T4> ToValueTuple<T1, T2, T3, T4>(this Tuple<T1, T2, T3, T4> value) =>
            ValueTuple.Create(value.Item1, value.Item2, value.Item3, value.Item4);

        public static ValueTuple<T1, T2, T3, T4, T5> ToValueTuple<T1, T2, T3, T4, T5>(this Tuple<T1, T2, T3, T4, T5> value) =>
            ValueTuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5);

        public static ValueTuple<T1, T2, T3, T4, T5, T6> ToValueTuple<T1, T2, T3, T4, T5, T6>(this Tuple<T1, T2, T3, T4, T5, T6> value) =>
            ValueTuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6);

        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7> ToValueTuple<T1, T2, T3, T4, T5, T6, T7>(this Tuple<T1, T2, T3, T4, T5, T6, T7> value) =>
            ValueTuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7);

        public static Tuple<T1> ToTuple<T1>(this ValueTuple<T1> value) =>
            Tuple.Create(value.Item1);

        public static Tuple<T1, T2> ToTuple<T1, T2>(this ValueTuple<T1, T2> value) =>
            Tuple.Create(value.Item1, value.Item2);

        public static Tuple<T1, T2, T3> ToTuple<T1, T2, T3>(this ValueTuple<T1, T2, T3> value) =>
            Tuple.Create(value.Item1, value.Item2, value.Item3);

        public static Tuple<T1, T2, T3, T4> ToTuple<T1, T2, T3, T4>(this ValueTuple<T1, T2, T3, T4> value) =>
            Tuple.Create(value.Item1, value.Item2, value.Item3, value.Item4);

        public static Tuple<T1, T2, T3, T4, T5> ToTuple<T1, T2, T3, T4, T5>(this ValueTuple<T1, T2, T3, T4, T5> value) =>
            Tuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5);

        public static Tuple<T1, T2, T3, T4, T5, T6> ToTuple<T1, T2, T3, T4, T5, T6>(this ValueTuple<T1, T2, T3, T4, T5, T6> value) =>
            Tuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6);

        public static Tuple<T1, T2, T3, T4, T5, T6, T7> ToTuple<T1, T2, T3, T4, T5, T6, T7>(this ValueTuple<T1, T2, T3, T4, T5, T6, T7> value) =>
            Tuple.Create(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7);
    }
}
