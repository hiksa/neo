using System;
using System.Linq;

namespace Neo.Extensions
{
    public static class EnumExtensions
    {
        public static bool HasAllFlags(this Enum value, params Enum[] flags) =>
            flags.All(flag => value.HasFlag(flag));

        public static bool HasAnyFlags(this Enum value, params Enum[] flags) =>
            flags.Any(flag => value.HasFlag(flag));

        public static bool HasNoFlags(this Enum value, params Enum[] flags) =>
            flags.All(flag => !value.HasFlag(flag));

        public static bool IsNoneOf(this Enum value, params Enum[] values) =>
            !values.Contains(value);

        public static bool IsOneOf(this Enum value, params Enum[] values) =>
            values.Contains(value);
    }
}
