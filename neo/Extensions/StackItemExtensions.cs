using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.SmartContract;
using Neo.VM;
using Neo.VM.Types;
using VMArray = Neo.VM.Types.Array;
using VMBoolean = Neo.VM.Types.Boolean;

namespace Neo.Extensions
{
    public static class StackItemExtensions
    {
        public static ContractParameter ToParameter(this StackItem item) => ToParameter(item, null);

        private static ContractParameter ToParameter(StackItem item, List<Tuple<StackItem, ContractParameter>> context)
        {
            ContractParameter parameter = null;
            switch (item)
            {
                case VMArray array:
                    if (context is null)
                    {
                        context = new List<Tuple<StackItem, ContractParameter>>();
                    }
                    else
                    {
                        parameter = context.FirstOrDefault(p => ReferenceEquals(p.Item1, item))?.Item2;
                    }

                    if (parameter is null)
                    {
                        parameter = new ContractParameter { Type = ContractParameterType.Array };
                        context.Add(new Tuple<StackItem, ContractParameter>(item, parameter));
                        parameter.Value = array.Select(p => ToParameter(p, context)).ToList();
                    }

                    break;
                case Map map:
                    if (context is null)
                    {
                        context = new List<Tuple<StackItem, ContractParameter>>();
                    }
                    else
                    {
                        parameter = context.FirstOrDefault(p => ReferenceEquals(p.Item1, item))?.Item2;
                    }

                    if (parameter is null)
                    {
                        parameter = new ContractParameter { Type = ContractParameterType.Map };
                        context.Add(new Tuple<StackItem, ContractParameter>(item, parameter));
                        parameter.Value = map
                            .Select(p => new KeyValuePair<ContractParameter, ContractParameter>(
                                StackItemExtensions.ToParameter(p.Key, context), 
                                ToParameter(p.Value, context)))
                            .ToList();
                    }

                    break;
                case VMBoolean _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.Boolean,
                        Value = item.GetBoolean()
                    };
                    break;
                case ByteArray _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.ByteArray,
                        Value = item.GetByteArray()
                    };
                    break;
                case Integer _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.Integer,
                        Value = item.GetBigInteger()
                    };
                    break;
                case InteropInterface _:
                    parameter = new ContractParameter
                    {
                        Type = ContractParameterType.InteropInterface
                    };
                    break;
                default:
                    throw new ArgumentException();
            }

            return parameter;
        }
    }
}
