using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Neo.IO.Json
{
    public class JNumber : JObject
    {
        public JNumber(double value = 0)
        {
            this.Value = value;
        }

        public double Value { get; private set; }

        public override bool AsBoolean() => this.Value != 0;

        public override T AsEnum<T>(bool ignoreCase = false)
        {
            var type = typeof(T);
            var typeInfo = type.GetTypeInfo();
            if (!typeInfo.IsEnum)
            {
                throw new InvalidCastException();
            }

            var underlyingType = typeInfo.GetEnumUnderlyingType();
            if (underlyingType == typeof(byte))
            {
                return (T)Enum.ToObject(type, (byte)this.Value);
            }
            else if (underlyingType == typeof(int))
            {
                return (T)Enum.ToObject(type, (int)this.Value);
            }
            else if (underlyingType == typeof(long))
            {
                return (T)Enum.ToObject(type, (long)this.Value);
            }
            else if (underlyingType == typeof(sbyte))
            {
                return (T)Enum.ToObject(type, (sbyte)this.Value);
            }
            else if (underlyingType == typeof(short))
            {
                return (T)Enum.ToObject(type, (short)this.Value);
            }
            else if (underlyingType == typeof(uint))
            {
                return (T)Enum.ToObject(type, (uint)this.Value);
            }
            else if (underlyingType == typeof(ulong))
            {
                return (T)Enum.ToObject(type, (ulong)this.Value);
            }
            else if (underlyingType == typeof(ushort))
            {
                return (T)Enum.ToObject(type, (ushort)this.Value);
            }

            throw new InvalidCastException();
        }

        public override double AsNumber() => this.Value;

        public override string AsString() => this.Value.ToString();
        
        public override bool CanConvertTo(Type type)
        {
            if (type == typeof(bool))
            {
                return true;
            }
            else if (type == typeof(double))
            {
                return true;
            }
            else if (type == typeof(string))
            {
                return true;
            }

            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsEnum && Enum.IsDefined(type, Convert.ChangeType(this.Value, typeInfo.GetEnumUnderlyingType())))
            {
                return true;
            }

            return false;
        }

        internal static JNumber Parse(TextReader reader)
        {
            JObject.SkipSpace(reader);
            var sb = new StringBuilder();
            while (true)
            {
                var c = (char)reader.Peek();
                if (c >= '0' && c <= '9' || c == '.' || c == '-')
                {
                    sb.Append(c);
                    reader.Read();
                }
                else
                {
                    break;
                }
            }

            return new JNumber(double.Parse(sb.ToString()));
        }

        public override string ToString() => this.Value.ToString();
        
        public DateTime ToTimestamp()
        {
            if (this.Value < 0 || this.Value > ulong.MaxValue)
            {
                throw new InvalidCastException();
            }

            return ((ulong)this.Value).ToDateTime();
        }
    }
}
