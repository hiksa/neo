using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Neo.IO.Json
{
    public class JArray : JObject, IList<JObject>
    {
        private List<JObject> items = new List<JObject>();

        public JArray(params JObject[] items) : this((IEnumerable<JObject>)items)
        {
        }

        public JArray(IEnumerable<JObject> items)
        {
            this.items.AddRange(items);
        }

        public int Count => this.items.Count;

        public bool IsReadOnly => false;

        public JObject this[int index]
        {
            get
            {
                return this.items[index];
            }

            set
            {
                this.items[index] = value;
            }
        }

        public void Add(JObject item) => this.items.Add(item);
        
        public void Clear() => this.items.Clear();

        public bool Contains(JObject item) => this.items.Contains(item);
        
        public void CopyTo(JObject[] array, int arrayIndex) => this.items.CopyTo(array, arrayIndex);        

        public IEnumerator<JObject> GetEnumerator() => this.items.GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        
        public int IndexOf(JObject item) => this.items.IndexOf(item);
        
        public void Insert(int index, JObject item) => this.items.Insert(index, item);
        
        internal new static JArray Parse(TextReader reader, int maxNestLevel)
        {
            if (maxNestLevel < 0)
            {
                throw new FormatException();
            }

            JObject.SkipSpace(reader);
            if (reader.Read() != '[')
            {
                throw new FormatException();
            }

            JObject.SkipSpace(reader);
            var array = new JArray();
            while (reader.Peek() != ']')
            {
                if (reader.Peek() == ',')
                {
                    reader.Read();
                }

                var obj = JObject.Parse(reader, maxNestLevel - 1);
                array.items.Add(obj);

                JObject.SkipSpace(reader);
            }

            reader.Read();
            return array;
        }

        public bool Remove(JObject item) => this.items.Remove(item);

        public void RemoveAt(int index) => this.items.RemoveAt(index);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('[');
            foreach (var item in this.items)
            {
                if (item == null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(item);
                }

                sb.Append(',');
            }

            if (this.items.Count == 0)
            {
                sb.Append(']');
            }
            else
            {
                sb[sb.Length - 1] = ']';
            }

            return sb.ToString();
        }
    }
}
