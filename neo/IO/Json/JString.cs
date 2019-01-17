using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;

namespace Neo.IO.Json
{
    public class JString : JObject
    {
        public JString(string value)
        {
            this.Value = value ?? throw new ArgumentNullException();
        }

        public string Value { get; private set; }

        public override bool AsBoolean()
        {
            switch (this.Value.ToLower())
            {
                case "0":
                case "f":
                case "false":
                case "n":
                case "no":
                case "off":
                    return false;
                default:
                    return true;
            }
        }

        public override T AsEnum<T>(bool ignoreCase = false)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), this.Value, ignoreCase);
            }
            catch
            {
                throw new InvalidCastException();
            }
        }

        public override double AsNumber()
        {
            try
            {
                return double.Parse(this.Value);
            }
            catch
            {
                throw new InvalidCastException();
            }
        }

        public override string AsString() => this.Value;

        public override bool CanConvertTo(Type type)
        {
            if (
                type.GetTypeInfo().IsEnum && Enum.IsDefined(type, this.Value)
                || type == typeof(bool)
                || type == typeof(double)
                || type == typeof(string))
            {
                return true;
            }

            return false;
        }

        internal static JString Parse(TextReader reader)
        {
            JObject.SkipSpace(reader);

            var buffer = new char[4];
            var firstChar = (char)reader.Read();
            if (firstChar != '\"' && firstChar != '\'')
            {
                throw new FormatException();
            }

            var sb = new StringBuilder();
            while (true)
            {
                var currentChar = (char)reader.Read();
                if (currentChar == 65535)
                {
                    throw new FormatException();
                }

                if (currentChar == firstChar)
                {
                    break;
                }

                if (currentChar == '\\')
                {
                    currentChar = (char)reader.Read();
                    switch (currentChar)
                    {
                        case 'u':
                            reader.Read(buffer, 0, 4);
                            currentChar = (char)short.Parse(new string(buffer), NumberStyles.HexNumber);
                            break;
                        case 'r':
                            currentChar = '\r';
                            break;
                        case 'n':
                            currentChar = '\n';
                            break;
                    }
                }

                sb.Append(currentChar);
            }

            return new JString(sb.ToString());
        }

        public override string ToString() => $"\"{JavaScriptEncoder.Default.Encode(this.Value)}\"";        
    }
}
