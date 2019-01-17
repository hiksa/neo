using System.Linq;
using Neo.Extensions;
using Neo.IO.Json;
using Neo.SmartContract;

namespace Neo.Wallets.NEP6
{
    internal class NEP6Contract : Contract
    {
        public string[] ParameterNames { get; set; }

        public bool Deployed { get; set; }

        public static NEP6Contract FromJson(JObject json)
        {
            if (json == null)
            {
                return null;
            }

            return new NEP6Contract
            {
                Script = json["script"].AsString().HexToBytes(),
                ParameterList = ((JArray)json["parameters"])
                    .Select(p => p["type"].AsEnum<ContractParameterType>())
                    .ToArray(),
                ParameterNames = ((JArray)json["parameters"])
                    .Select(p => p["name"].AsString())
                    .ToArray(),
                Deployed = json["deployed"].AsBoolean()
            };
        }

        public JObject ToJson()
        {
            var contract = new JObject();
            contract["script"] = this.Script.ToHexString();

            var parameters = this.ParameterList.Zip(
                this.ParameterNames, 
                (type, name) =>
                {
                    JObject parameter = new JObject();
                    parameter["name"] = name;
                    parameter["type"] = type;
                    return parameter;
                });

            contract["parameters"] = new JArray(parameters);
            contract["deployed"] = this.Deployed;
            return contract;
        }
    }
}
