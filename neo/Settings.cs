using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Neo.Network.P2P.Payloads;

namespace Neo
{
    public class ProtocolSettings
    {
        static ProtocolSettings()
        {
            var section = new ConfigurationBuilder()
                .AddJsonFile("protocol.json")
                .Build()
                .GetSection("ProtocolConfiguration");

            ProtocolSettings.Default = new ProtocolSettings(section);
        }

        private ProtocolSettings(IConfigurationSection section)
        {
            var magic = section.GetSection("Magic");
            this.Magic = uint.Parse(magic.Value);

            var addressVersion = section.GetSection("AddressVersion");
            this.AddressVersion = byte.Parse(addressVersion.Value);

            var standByValidators = section.GetSection("StandbyValidators");
            this.StandbyValidators = standByValidators.GetChildren().Select(p => p.Value).ToArray();

            var seedList = section.GetSection("SeedList");
            this.SeedList = seedList.GetChildren().Select(p => p.Value).ToArray();

            var systemFee = section.GetSection("SystemFee");
            this.SystemFee = systemFee.GetChildren().ToDictionary(p => (TransactionType)Enum.Parse(typeof(TransactionType), p.Key, true), p => Fixed8.Parse(p.Value));

            var secondsPerBlock = section.GetSection("SecondsPerBlock");
            this.SecondsPerBlock = this.GetValueOrDefault(secondsPerBlock, 15u, p => uint.Parse(p));

            var lowPriortyThreshold = section.GetSection("LowPriorityThreshold");
            this.LowPriorityThreshold = this.GetValueOrDefault(lowPriortyThreshold, Fixed8.FromDecimal(0.001m), p => Fixed8.Parse(p));
        }

        public static ProtocolSettings Default { get; }

        public uint Magic { get; }

        public byte AddressVersion { get; }

        public string[] StandbyValidators { get; }

        public string[] SeedList { get; }

        public IReadOnlyDictionary<TransactionType, Fixed8> SystemFee { get; }

        public Fixed8 LowPriorityThreshold { get; }

        public uint SecondsPerBlock { get; }

        internal T GetValueOrDefault<T>(IConfigurationSection section, T defaultValue, Func<string, T> selector) =>
            section.Value == null
                ? defaultValue
                : selector(section.Value);
    }
}
