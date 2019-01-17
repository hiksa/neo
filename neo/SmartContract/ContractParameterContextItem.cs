using System.Collections.Generic;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO.Json;

namespace Neo.SmartContract
{
    public class ContextItem
    {
        public ContextItem(Contract contract)
        {
            this.Script = contract.Script;
            this.Parameters = contract.ParameterList.Select(p => new ContractParameter { Type = p }).ToArray();
        }

        private ContextItem()
        {
        }

        public byte[] Script { get; private set; }

        public ContractParameter[] Parameters { get; private set; }

        public Dictionary<ECPoint, byte[]> Signatures { get; set; }

        public static ContextItem FromJson(JObject json)
        {
            return new ContextItem
            {
                Script = json["script"]?.AsString().HexToBytes(),
                Parameters = ((JArray)json["parameters"]).Select(p => ContractParameter.FromJson(p)).ToArray(),
                Signatures = json["signatures"]?.Properties
                    .Select(p => new
                    {
                        PublicKey = ECPoint.Parse(p.Key, ECCurve.Secp256r1),
                        Signature = p.Value.AsString().HexToBytes()
                    })
                    .ToDictionary(p => p.PublicKey, p => p.Signature)
            };
        }

        public JObject ToJson()
        {
            var json = new JObject();
            if (this.Script != null)
            {
                json["script"] = this.Script.ToHexString();
            }

            json["parameters"] = new JArray(this.Parameters.Select(p => p.ToJson()));
            if (this.Signatures != null)
            {
                json["signatures"] = new JObject();
                foreach (var signature in this.Signatures)
                {
                    json["signatures"][signature.Key.ToString()] = signature.Value.ToHexString();
                }
            }

            return json;
        }
    }
}
