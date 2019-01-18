using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Neo.IO.Json
{
    public class JObject
    {
        public static readonly JObject Null = null;
        private Dictionary<string, JObject> properties = new Dictionary<string, JObject>();

        public IReadOnlyDictionary<string, JObject> Properties => this.properties;

        public JObject this[string name]
        {
            get
            {
                this.properties.TryGetValue(name, out JObject value);
                return value;
            }

            set
            {
                this.properties[name] = value;
            }
        }

        public virtual bool AsBoolean()
        {
            throw new InvalidCastException();
        }

        public bool AsBooleanOrDefault(bool value = false)
        {
            if (!this.CanConvertTo(typeof(bool)))
            {
                return value;
            }

            return this.AsBoolean();
        }

        public virtual T AsEnum<T>(bool ignoreCase = false)
        {
            throw new InvalidCastException();
        }

        public T AsEnumOrDefault<T>(T value = default(T), bool ignoreCase = false)
        {
            if (!this.CanConvertTo(typeof(T)))
            {
                return value;
            }

            return this.AsEnum<T>(ignoreCase);
        }

        public virtual double AsNumber()
        {
            throw new InvalidCastException();
        }

        public double AsNumberOrDefault(double value = 0)
        {
            if (!this.CanConvertTo(typeof(double)))
            {
                return value;
            }

            return this.AsNumber();
        }

        public virtual string AsString()
        {
            throw new InvalidCastException();
        }

        public string AsStringOrDefault(string value = null)
        {
            if (!this.CanConvertTo(typeof(string)))
            {
                return value;
            }

            return this.AsString();
        }

        public virtual bool CanConvertTo(Type type)
        {
            return false;
        }

        public bool ContainsProperty(string key) => this.properties.ContainsKey(key);

        public static JObject Parse(TextReader reader, int maxNestDepth = 100)
        {
            if (maxNestDepth < 0)
            {
                throw new FormatException();
            }

            JObject.SkipSpace(reader);
            var firstChar = (char)reader.Peek();
            if (firstChar == '\"' || firstChar == '\'')
            {
                return JString.Parse(reader);
            }

            if (firstChar == '[')
            {
                return JArray.Parse(reader, maxNestDepth);
            }

            if ((firstChar >= '0' && firstChar <= '9') || firstChar == '-')
            {
                return JNumber.Parse(reader);
            }

            if (firstChar == 't' || firstChar == 'f')
            {
                return JBoolean.Parse(reader);
            }

            if (firstChar == 'n')
            {
                return ParseNull(reader);
            }

            if (reader.Read() != '{')
            {
                throw new FormatException();
            }

            JObject.SkipSpace(reader);
            var obj = new JObject();
            while (reader.Peek() != '}')
            {
                if (reader.Peek() == ',')
                {
                    reader.Read();
                }

                JObject.SkipSpace(reader);
                var name = JString.Parse(reader).Value;
                JObject.SkipSpace(reader);

                if (reader.Read() != ':')
                {
                    throw new FormatException();
                }

                var value = Parse(reader, maxNestDepth - 1);
                obj.properties.Add(name, value);
                JObject.SkipSpace(reader);
            }

            reader.Read();
            return obj;
        }

        public static JObject Parse(string value, int maxNest = 100)
        {
            using (var reader = new StringReader(value))
            {
                return JObject.Parse(reader, maxNest);
            }
        }

        private static JObject ParseNull(TextReader reader)
        {
            var firstChar = (char)reader.Read();
            if (firstChar == 'n')
            {
                var c2 = reader.Read();
                var c3 = reader.Read();
                var c4 = reader.Read();
                if (c2 == 'u' && c3 == 'l' && c4 == 'l')
                {
                    return null;
                }
            }

            throw new FormatException();
        }

        protected static void SkipSpace(TextReader reader)
        {
            while (reader.Peek() == ' ' || reader.Peek() == '\t' || reader.Peek() == '\r' || reader.Peek() == '\n')
            {
                reader.Read();
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            foreach (var pair in this.properties)
            {
                sb.Append('"');
                sb.Append(pair.Key);
                sb.Append('"');
                sb.Append(':');
                if (pair.Value == null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(pair.Value);
                }

                sb.Append(',');
            }

            if (this.properties.Count == 0)
            {
                sb.Append('}');
            }
            else
            {
                sb[sb.Length - 1] = '}';
            }

            return sb.ToString();
        }

        public static implicit operator JObject(Enum value) => new JString(value.ToString());

        public static implicit operator JObject(JObject[] value) => new JArray(value);

        public static implicit operator JObject(bool value) => new JBoolean(value);

        public static implicit operator JObject(double value) => new JNumber(value);

        public static implicit operator JObject(string value) => 
            value == null ? null : new JString(value);
    }
}
