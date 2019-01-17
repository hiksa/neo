using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Neo.IO.Caching
{
    public class ReflectionCache<T> : Dictionary<T, Type>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public ReflectionCache()
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <typeparam name="EnumType">Enum type</typeparam>
        public static ReflectionCache<T> CreateFromEnum<EnumType>() 
            where EnumType : struct, IConvertible
        {
            var enumType = typeof(EnumType);
            if (!enumType.GetTypeInfo().IsEnum)
            {
                throw new ArgumentException("K must be an enumerated type");
            }

            var cache = new ReflectionCache<T>();
            foreach (var t in Enum.GetValues(enumType))
            {
                var memberInfo = enumType.GetMember(t.ToString());
                if (memberInfo == null || memberInfo.Length != 1)
                {
                    throw new FormatException();
                }

                var attribute = memberInfo[0]
                    .GetCustomAttributes(typeof(ReflectionCacheAttribute), false)
                    .Cast<ReflectionCacheAttribute>()
                    .FirstOrDefault();

                if (attribute == null)
                {
                    throw new FormatException();
                }

                cache.Add((T)t, attribute.Type);
            }

            return cache;
        }

        /// <summary>
        /// Create object from key
        /// </summary>
        /// <param name="key">Key</param>
        /// <param name="def">Default value</param>
        public object CreateInstance(T key, object def = null)
        {
            if (this.TryGetValue(key, out Type tp))
            {
                return Activator.CreateInstance(tp);
            }

            return def;
        }

        /// <summary>
        /// Create object from key
        /// </summary>
        /// <typeparam name="K">Type</typeparam>
        /// <param name="key">Key</param>
        /// <param name="def">Default value</param>
        public K CreateInstance<K>(T key, K def = default(K))
        {
            if (this.TryGetValue(key, out Type tp))
            {
                return (K)Activator.CreateInstance(tp);
            }

            return def;
        }
    }
}