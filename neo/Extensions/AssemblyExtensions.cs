using System.Linq;
using System.Reflection;

namespace Neo.Extensions
{
    public static class AssemblyExtensions
    {
        internal static string GetVersion(this Assembly assembly)
        {
            var attribute = assembly.CustomAttributes
                .FirstOrDefault(p => p.AttributeType == typeof(AssemblyInformationalVersionAttribute));

            if (attribute == null)
            {
                return assembly.GetName().Version.ToString(3);
            }

            return (string)attribute.ConstructorArguments[0].Value;
        }
    }
}
