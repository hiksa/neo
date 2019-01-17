using System;
using System.IO;

namespace Neo.IO.Json
{
    public class JBoolean : JObject
    {
        public JBoolean(bool value = false)
        {
            this.Value = value;
        }

        public bool Value { get; private set; }

        public override bool AsBoolean() => this.Value;

        public override string AsString() => this.Value.ToString().ToLower();

        public override bool CanConvertTo(Type type)
        {
            if (type == typeof(bool))
            {
                return true;
            }

            if (type == typeof(string))
            {
                return true;
            }

            return false;
        }

        internal static JBoolean Parse(TextReader reader)
        {
            JObject.SkipSpace(reader);
            var firstChar = (char)reader.Read();
            if (firstChar == 't')
            {
                var c2 = reader.Read();
                var c3 = reader.Read();
                var c4 = reader.Read();
                if (c2 == 'r' && c3 == 'u' && c4 == 'e')
                {
                    return new JBoolean(true);
                }
            }
            else if (firstChar == 'f')
            {
                var c2 = reader.Read();
                var c3 = reader.Read();
                var c4 = reader.Read();
                var c5 = reader.Read();
                if (c2 == 'a' && c3 == 'l' && c4 == 's' && c5 == 'e')
                {
                    return new JBoolean(false);
                }
            }

            throw new FormatException();
        }

        public override string ToString() => this.Value.ToString().ToLower();
    }
}
