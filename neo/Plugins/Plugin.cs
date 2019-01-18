using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Neo.Network.P2P.Payloads;

namespace Neo.Plugins
{
    public abstract class Plugin
    {
        public static readonly List<Plugin> Plugins = new List<Plugin>();

        internal static readonly List<IPolicyPlugin> Policies = new List<IPolicyPlugin>();
        internal static readonly List<IRpcPlugin> RpcPlugins = new List<IRpcPlugin>();
        internal static readonly List<IPersistencePlugin> PersistencePlugins = new List<IPersistencePlugin>();

        private static readonly List<ILogPlugin> Loggers = new List<ILogPlugin>();
        private static readonly string PluginsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "Plugins");
        private static readonly FileSystemWatcher ConfigWatcher;

        private static int suspend = 0;

        static Plugin()
        {
            if (Directory.Exists(PluginsPath))
            {
                Plugin.ConfigWatcher = new FileSystemWatcher(PluginsPath, "*.json")
                {
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                };

                Plugin.ConfigWatcher.Changed += ConfigWatcherChanged;
                Plugin.ConfigWatcher.Created += ConfigWatcherChanged;
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainAssemblyResolve;
            }
        }

        protected Plugin()
        {
            Plugin.Plugins.Add(this);

            if (this is ILogPlugin logger)
            {
                Plugin.Loggers.Add(logger);
            }

            if (this is IPolicyPlugin policy)
            {
                Plugin.Policies.Add(policy);
            }

            if (this is IRpcPlugin rpc)
            {
                Plugin.RpcPlugins.Add(rpc);
            }

            if (this is IPersistencePlugin persistence)
            {
                Plugin.PersistencePlugins.Add(persistence);
            }

            this.Configure();
        }

        public virtual string Name => this.GetType().Name;

        public virtual Version Version => this.GetType().Assembly.GetName().Version;

        public virtual string ConfigFile => Path.Combine(
            Plugin.PluginsPath,
            this.GetType().Assembly.GetName().Name,
            "config.json");

        protected static NeoSystem System { get; private set; }

        public static bool CheckPolicy(Transaction tx)
        {
            foreach (var plugin in Plugin.Policies)
            {
                if (!plugin.FilterForMemoryPool(tx))
                {
                    return false;
                }
            }

            return true;
        }

        public static void Log(string source, LogLevel level, string message)
        {
            foreach (var plugin in Plugin.Loggers)
            {
                plugin.Log(source, level, message);
            }
        }

        public static bool SendMessage(object message)
        {
            foreach (var plugin in Plugin.Plugins)
            {
                if (plugin.OnMessage(message))
                {
                    return true;
                }
            }

            return false;
        }

        public abstract void Configure();

        internal static void LoadPlugins(NeoSystem system)
        {
            Plugin.System = system;
            if (!Directory.Exists(PluginsPath))
            {
                return;
            }

            var files = Directory.EnumerateFiles(Plugin.PluginsPath, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (var filename in files)
            {
                var assembly = Assembly.LoadFile(filename);
                foreach (var type in assembly.ExportedTypes)
                {
                    if (!type.IsSubclassOf(typeof(Plugin)) || type.IsAbstract)
                    {
                        continue;
                    }

                    var constructorInfo = type.GetConstructor(Type.EmptyTypes);
                    try
                    {
                        constructorInfo?.Invoke(null);
                    }
                    catch (Exception ex)
                    {
                        Log(nameof(Plugin), LogLevel.Error, $"Failed to initialize plugin: {ex.Message}");
                    }
                }
            }
        }

        protected static void SuspendNodeStartup()
        {
            Interlocked.Increment(ref suspend);
            Plugin.System.SuspendNodeStartup();
        }

        protected static bool ResumeNodeStartup()
        {
            if (Interlocked.Decrement(ref suspend) != 0)
            {
                return false;
            }

            Plugin.System.ResumeNodeStartup();
            return true;
        }

        protected IConfigurationSection GetConfiguration() =>
            new ConfigurationBuilder()
                .AddJsonFile(this.ConfigFile, optional: true)
                .Build()
                .GetSection("PluginConfiguration");

        protected virtual bool OnMessage(object message) => false;

        protected void Log(string message, LogLevel level = LogLevel.Info) =>
            Plugin.Log($"{nameof(Plugin)}:{Name}", level, message);

        private static void ConfigWatcherChanged(object sender, FileSystemEventArgs e)
        {
            foreach (var plugin in Plugins)
            {
                if (plugin.ConfigFile == e.FullPath)
                {
                    plugin.Configure();
                    plugin.Log($"Reloaded config for {plugin.Name}");
                    break;
                }
            }
        }

        private static Assembly CurrentDomainAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (args.Name.Contains(".resources"))
            {
                return null;
            }

            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
            if (assembly != null)
            {
                return assembly;
            }

            var assemblyName = new AssemblyName(args.Name);
            var filename = assemblyName.Name + ".dll";

            try
            {
                return Assembly.LoadFrom(filename);
            }
            catch (Exception ex)
            {
                Log(nameof(Plugin), LogLevel.Error, $"Failed to resolve assembly or its dependency: {ex.Message}");
                return null;
            }
        }
    }
}
