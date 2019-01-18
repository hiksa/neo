using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Neo.IO;

namespace Neo.Extensions
{
    public static class EnumerableExtensions
    {
        public static string ToHexString(this IEnumerable<byte> value)
        {
            var sb = new StringBuilder();
            foreach (var b in value)
            {
                sb.AppendFormat("{0:x2}", b);
            }

            return sb.ToString();
        }

        public static Fixed8 Sum(this IEnumerable<Fixed8> source)
        {
            long sum = 0;
            checked
            {
                foreach (var item in source)
                {
                    sum += item.Value;
                }
            }

            return new Fixed8(sum);
        }

        public static Fixed8 Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, Fixed8> selector) =>
            source.Select(selector).Sum();

        internal static long WeightedAverage<T>(
            this IEnumerable<T> source, 
            Func<T, long> valueSelector, 
            Func<T, long> weightSelector)
        {
            long sumWeight = 0;
            long sumValue = 0;
            foreach (var item in source)
            {
                long weight = weightSelector(item);
                sumWeight += weight;
                sumValue += valueSelector(item) * weight;
            }

            return sumValue == 0 ? 0 : sumValue / sumWeight;
        }

        internal static IEnumerable<TResult> WeightedFilter<T, TResult>(
            this IList<T> source, 
            double start, 
            double end, 
            Func<T, long> weightSelector, 
            Func<T, long, TResult> resultSelector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (start < 0 || start > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (end < start || start + end > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(end));
            }

            if (weightSelector == null)
            {
                throw new ArgumentNullException(nameof(weightSelector));
            }

            if (resultSelector == null)
            {
                throw new ArgumentNullException(nameof(resultSelector));
            }

            if (source.Count == 0 || start == end)
            {
                yield break;
            }

            double amount = source.Sum(weightSelector);
            long sum = 0;
            double current = 0;

            foreach (var item in source)
            {
                if (current >= end)
                {
                    break;
                }

                long weight = weightSelector(item);
                sum += weight;
                double old = current;

                current = sum / amount;
                if (current <= start)
                {
                    continue;
                }

                if (old < start)
                {
                    if (current > end)
                    {
                        weight = (long)((end - start) * amount);
                    }
                    else
                    {
                        weight = (long)((current - start) * amount);
                    }
                }
                else if (current > end)
                {
                    weight = (long)((end - old) * amount);
                }

                yield return resultSelector(item, weight);
            }
        }
        
        public static byte[] ToByteArray<T>(this T[] value) where T : ISerializable
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(value);
                writer.Flush();
                return ms.ToArray();
            }
        }

        internal static int GetVarSize<T>(this T[] value)
        {
            var valueSize = 0;
            var type = typeof(T);
            if (typeof(ISerializable).IsAssignableFrom(type))
            {
                valueSize = value.OfType<ISerializable>().Sum(p => p.Size);
            }
            else if (type.GetTypeInfo().IsEnum)
            {
                var elementSize = 0;
                var underlyingType = type.GetTypeInfo().GetEnumUnderlyingType();
                if (underlyingType == typeof(sbyte) || underlyingType == typeof(byte))
                {
                    elementSize = 1;
                }
                else if (underlyingType == typeof(short) || underlyingType == typeof(ushort))
                {
                    elementSize = 2;
                }
                else if (underlyingType == typeof(int) || underlyingType == typeof(uint))
                {
                    elementSize = 4;
                }
                else
                {
                    elementSize = 8;
                }

                valueSize = value.Length * elementSize;
            }
            else
            {
                valueSize = value.Length * Marshal.SizeOf<T>();
            }

            return Neo.IO.Helper.GetVarSize(value.Length) + valueSize;
        }
    }
}
