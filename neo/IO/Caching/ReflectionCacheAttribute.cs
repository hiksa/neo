using System;

namespace Neo.IO.Caching
{
    public class ReflectionCacheAttribute : Attribute
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="type">Type</param>
        public ReflectionCacheAttribute(Type type)
        {
            this.Type = type;
        }

        /// <summary>
        /// Type
        /// </summary>
        public Type Type { get; private set; }
    }
}