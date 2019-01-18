using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.IO.Json;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.VM;

namespace Neo.SmartContract
{
    public class ContractParametersContext
    {
        public readonly IVerifiable Verifiable;

        private readonly Dictionary<UInt160, ContextItem> contextItems;

        private UInt160[] scriptHashes = null;

        public ContractParametersContext(IVerifiable verifiable)
        {
            this.Verifiable = verifiable;
            this.contextItems = new Dictionary<UInt160, ContextItem>();
        }

        public bool Completed
        {
            get
            {
                if (this.contextItems.Count < this.ScriptHashes.Count)
                {
                    return false;
                }

                return this.contextItems.Values.All(p => p != null && p.Parameters.All(q => q.Value != null));
            }
        }

        public IReadOnlyList<UInt160> ScriptHashes
        {
            get
            {
                if (this.scriptHashes == null)
                {
                    using (var snapshot = Blockchain.Instance.GetSnapshot())
                    {
                        this.scriptHashes = this.Verifiable.GetScriptHashesForVerifying(snapshot);
                    }
                }

                return this.scriptHashes;
            }
        }

        public static ContractParametersContext Parse(string value) => FromJson(JObject.Parse(value));

        public static ContractParametersContext FromJson(JObject json)
        {
            var verifiable = typeof(ContractParametersContext)
                .GetTypeInfo()
                .Assembly
                .CreateInstance(json["type"].AsString()) as IVerifiable;

            if (verifiable == null)
            {
                throw new FormatException();
            }

            using (var ms = new MemoryStream(json["hex"].AsString().HexToBytes(), false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                verifiable.DeserializeUnsigned(reader);
            }

            var context = new ContractParametersContext(verifiable);
            foreach (var property in json["items"].Properties)
            {
                var key = UInt160.Parse(property.Key);
                var value = ContextItem.FromJson(property.Value);

                context.contextItems.Add(key, value);
            }

            return context;
        }

        public bool Add(Contract contract, int index, object parameter)
        {
            var contextItem = this.CreateItem(contract);
            if (contextItem == null)
            {
                return false;
            }

            contextItem.Parameters[index].Value = parameter;
            return true;
        }

        public bool AddSignature(Contract contract, ECPoint pubkey, byte[] signature)
        {
            if (contract.Script.IsMultiSigContract())
            {
                var contextItem = this.CreateItem(contract);
                if (contextItem == null || contextItem.Parameters.All(p => p.Value != null))
                {
                    return false;
                }

                if (contextItem.Signatures == null)
                {
                    contextItem.Signatures = new Dictionary<ECPoint, byte[]>();
                }
                else if (contextItem.Signatures.ContainsKey(pubkey))
                {
                    return false;
                }

                var points = new List<ECPoint>();
                {
                    int i = 0;
                    switch (contract.Script[i++])
                    {
                        case 1:
                            ++i;
                            break;
                        case 2:
                            i += 2;
                            break;
                    }

                    while (contract.Script[i++] == 33)
                    {
                        var encodedPoint = contract.Script.Skip(i).Take(33).ToArray();
                        var point = ECPoint.DecodePoint(encodedPoint, ECCurve.Secp256r1);
                        points.Add(point);
                        i += 33;
                    }
                }

                if (!points.Contains(pubkey))
                {
                    return false;
                }

                contextItem.Signatures.Add(pubkey, signature);
                if (contextItem.Signatures.Count == contract.ParameterList.Length)
                {
                    Dictionary<ECPoint, int> dic = points
                        .Select((p, i) => new
                        {
                            PublicKey = p,
                            Index = i
                        })
                        .ToDictionary(p => p.PublicKey, p => p.Index);

                    var signatures = contextItem.Signatures
                        .Select(p => new
                        {
                            Signature = p.Value,
                            Index = dic[p.Key]
                        })
                        .OrderByDescending(p => p.Index)
                        .Select(p => p.Signature)
                        .ToArray();

                    for (int i = 0; i < signatures.Length; i++)
                    {
                        if (!this.Add(contract, i, signatures[i]))
                        {
                            throw new InvalidOperationException();
                        }
                    }

                    contextItem.Signatures = null;
                }

                return true;
            }
            else
            {
                int index = -1;
                for (int i = 0; i < contract.ParameterList.Length; i++)
                {
                    if (contract.ParameterList[i] == ContractParameterType.Signature)
                    {
                        if (index >= 0)
                        {
                            throw new NotSupportedException();
                        }
                        else
                        {
                            index = i;
                        }
                    }
                }

                if (index == -1)
                {
                    // unable to find ContractParameterType.Signature in contract.ParameterList 
                    // return now to prevent array index out of bounds exception
                    return false;
                }

                return this.Add(contract, index, signature);
            }
        }

        public ContractParameter GetParameter(UInt160 scriptHash, int index) =>
            this.GetParameters(scriptHash)?[index];

        public IReadOnlyList<ContractParameter> GetParameters(UInt160 scriptHash)
        {
            if (!this.contextItems.TryGetValue(scriptHash, out ContextItem item))
            {
                return null;
            }

            return item.Parameters;
        }

        public Witness[] GetWitnesses()
        {
            if (!this.Completed)
            {
                throw new InvalidOperationException();
            }

            var witnesses = new Witness[this.ScriptHashes.Count];
            for (int i = 0; i < this.ScriptHashes.Count; i++)
            {
                var contextItem = this.contextItems[this.ScriptHashes[i]];
                using (var sb = new ScriptBuilder())
                {
                    foreach (var parameter in contextItem.Parameters.Reverse())
                    {
                        sb.EmitPush(parameter);
                    }

                    witnesses[i] = new Witness
                    {
                        InvocationScript = sb.ToArray(),
                        VerificationScript = contextItem.Script ?? new byte[0]
                    };
                }
            }

            return witnesses;
        }

        public JObject ToJson()
        {
            var json = new JObject();
            json["type"] = this.Verifiable.GetType().FullName;

            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                this.Verifiable.SerializeUnsigned(writer);
                writer.Flush();
                json["hex"] = ms.ToArray().ToHexString();
            }

            json["items"] = new JObject();
            foreach (var item in this.contextItems)
            {
                json["items"][item.Key.ToString()] = item.Value.ToJson();
            }

            return json;
        }

        public override string ToString() => this.ToJson().ToString();

        private ContextItem CreateItem(Contract contract)
        {
            if (this.contextItems.TryGetValue(contract.ScriptHash, out ContextItem item))
            {
                return item;
            }

            if (!this.ScriptHashes.Contains(contract.ScriptHash))
            {
                return null;
            }

            item = new ContextItem(contract);
            this.contextItems.Add(contract.ScriptHash, item);
            return item;
        }
    }
}
