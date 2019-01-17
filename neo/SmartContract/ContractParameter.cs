using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO.Json;

namespace Neo.SmartContract
{
    public class ContractParameter
    {
        public ContractParameter()
        {
        }

        public ContractParameter(ContractParameterType type)
        {
            this.Type = type;
            switch (type)
            {
                case ContractParameterType.Signature:
                    this.Value = new byte[64];
                    break;
                case ContractParameterType.Boolean:
                    this.Value = false;
                    break;
                case ContractParameterType.Integer:
                    this.Value = 0;
                    break;
                case ContractParameterType.Hash160:
                    this.Value = new UInt160();
                    break;
                case ContractParameterType.Hash256:
                    this.Value = new UInt256();
                    break;
                case ContractParameterType.ByteArray:
                    this.Value = new byte[0];
                    break;
                case ContractParameterType.PublicKey:
                    this.Value = ECCurve.Secp256r1.G;
                    break;
                case ContractParameterType.String:
                    this.Value = string.Empty;
                    break;
                case ContractParameterType.Array:
                    this.Value = new List<ContractParameter>();
                    break;
                case ContractParameterType.Map:
                    this.Value = new List<KeyValuePair<ContractParameter, ContractParameter>>();
                    break;
                default:
                    throw new ArgumentException();
            }
        }

        public ContractParameterType Type { get; set; }

        public object Value { get; set; }

        public static ContractParameter FromJson(JObject json)
        {
            var parameter = new ContractParameter
            {
                Type = json["type"].AsEnum<ContractParameterType>()
            };

            if (json["value"] != null)
            {
                switch (parameter.Type)
                {
                    case ContractParameterType.Signature:
                    case ContractParameterType.ByteArray:
                        parameter.Value = json["value"].AsString().HexToBytes();
                        break;
                    case ContractParameterType.Boolean:
                        parameter.Value = json["value"].AsBoolean();
                        break;
                    case ContractParameterType.Integer:
                        parameter.Value = BigInteger.Parse(json["value"].AsString());
                        break;
                    case ContractParameterType.Hash160:
                        parameter.Value = UInt160.Parse(json["value"].AsString());
                        break;
                    case ContractParameterType.Hash256:
                        parameter.Value = UInt256.Parse(json["value"].AsString());
                        break;
                    case ContractParameterType.PublicKey:
                        parameter.Value = ECPoint.Parse(json["value"].AsString(), ECCurve.Secp256r1);
                        break;
                    case ContractParameterType.String:
                        parameter.Value = json["value"].AsString();
                        break;
                    case ContractParameterType.Array:
                        parameter.Value = ((JArray)json["value"]).Select(p => FromJson(p)).ToList();
                        break;
                    case ContractParameterType.Map:
                        parameter.Value = ((JArray)json["value"])
                            .Select(p => new KeyValuePair<ContractParameter, ContractParameter>(
                                ContractParameter.FromJson(p["key"]), 
                                ContractParameter.FromJson(p["value"])))
                            .ToList();
                        break;
                    default:
                        throw new ArgumentException();
                }
            }

            return parameter;
        }

        public override string ToString() => ToString(this, null);

        public void SetValue(string text)
        {
            switch (this.Type)
            {
                case ContractParameterType.Signature:
                    var signature = text.HexToBytes();
                    if (signature.Length != 64)
                    {
                        throw new FormatException();
                    }

                    this.Value = signature;
                    break;
                case ContractParameterType.Boolean:
                    this.Value = string.Equals(text, bool.TrueString, StringComparison.OrdinalIgnoreCase);
                    break;
                case ContractParameterType.Integer:
                    this.Value = BigInteger.Parse(text);
                    break;
                case ContractParameterType.Hash160:
                    this.Value = UInt160.Parse(text);
                    break;
                case ContractParameterType.Hash256:
                    this.Value = UInt256.Parse(text);
                    break;
                case ContractParameterType.ByteArray:
                    this.Value = text.HexToBytes();
                    break;
                case ContractParameterType.PublicKey:
                    this.Value = ECPoint.Parse(text, ECCurve.Secp256r1);
                    break;
                case ContractParameterType.String:
                    this.Value = text;
                    break;
                default:
                    throw new ArgumentException();
            }
        }

        public JObject ToJson() => ToJson(this, null);

        private static JObject ToJson(ContractParameter parameter, HashSet<ContractParameter> context)
        {
            var json = new JObject();
            json["type"] = parameter.Type;
            if (parameter.Value != null)
            {
                switch (parameter.Type)
                {
                    case ContractParameterType.Signature:
                    case ContractParameterType.ByteArray:
                        json["value"] = ((byte[])parameter.Value).ToHexString();
                        break;
                    case ContractParameterType.Boolean:
                        json["value"] = (bool)parameter.Value;
                        break;
                    case ContractParameterType.Integer:
                    case ContractParameterType.Hash160:
                    case ContractParameterType.Hash256:
                    case ContractParameterType.PublicKey:
                    case ContractParameterType.String:
                        json["value"] = parameter.Value.ToString();
                        break;
                    case ContractParameterType.Array:
                        if (context is null)
                        {
                            context = new HashSet<ContractParameter>();
                        }
                        else if (context.Contains(parameter))
                        {
                            throw new InvalidOperationException();
                        }

                        context.Add(parameter);
                        var parameters = ((IList<ContractParameter>)parameter.Value)
                            .Select(p => ContractParameter.ToJson(p, context));

                        json["value"] = new JArray(parameters);
                        break;
                    case ContractParameterType.Map:
                        if (context is null)
                        {
                            context = new HashSet<ContractParameter>();
                        }
                        else if (context.Contains(parameter))
                        {
                            throw new InvalidOperationException();
                        }

                        context.Add(parameter);
                        var items = ((IList<KeyValuePair<ContractParameter, ContractParameter>>)parameter.Value)
                            .Select(p =>
                            {
                                var item = new JObject();
                                item["key"] = ToJson(p.Key, context);
                                item["value"] = ToJson(p.Value, context);
                                return item;
                            });

                        json["value"] = new JArray(items);
                        break;
                }
            }

            return json;
        }

        private static string ToString(ContractParameter parameter, HashSet<ContractParameter> context)
        {
            switch (parameter.Value)
            {
                case null:
                    return "(null)";
                case byte[] data:
                    return data.ToHexString();
                case IList<ContractParameter> data:
                    if (context is null)
                    {
                        context = new HashSet<ContractParameter>();
                    }

                    if (context.Contains(parameter))
                    {
                        return "(array)";
                    }
                    else
                    {
                        context.Add(parameter);
                        var sb = new StringBuilder();
                        sb.Append('[');
                        foreach (var item in data)
                        {
                            sb.Append(ToString(item, context));
                            sb.Append(", ");
                        }

                        if (data.Count > 0)
                        {
                            sb.Length -= 2;
                        }

                        sb.Append(']');
                        return sb.ToString();
                    }

                case IList<KeyValuePair<ContractParameter, ContractParameter>> data:
                    if (context is null)
                    {
                        context = new HashSet<ContractParameter>();
                    }

                    if (context.Contains(parameter))
                    {
                        return "(map)";
                    }
                    else
                    {
                        context.Add(parameter);
                        var sb = new StringBuilder();
                        sb.Append('[');
                        foreach (var item in data)
                        {
                            sb.Append('{');
                            sb.Append(ContractParameter.ToString(item.Key, context));
                            sb.Append(',');
                            sb.Append(ContractParameter.ToString(item.Value, context));
                            sb.Append('}');
                            sb.Append(", ");
                        }

                        if (data.Count > 0)
                        {
                            sb.Length -= 2;
                        }

                        sb.Append(']');
                        return sb.ToString();
                    }

                default:
                    return parameter.Value.ToString();
            }
        }
    }
}
