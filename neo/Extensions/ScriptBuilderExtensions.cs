﻿using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.SmartContract;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Neo.Extensions
{
    public static class ScriptBuilderExtensions
    {
        public static ScriptBuilder Emit(this ScriptBuilder sb, params OpCode[] ops)
        {
            foreach (OpCode op in ops)
            {
                sb.Emit(op);
            }

            return sb;
        }

        public static ScriptBuilder EmitAppCall(this ScriptBuilder sb, UInt160 scriptHash, bool useTailCall = false) =>
            sb.EmitAppCall(scriptHash.ToArray(), useTailCall);        

        public static ScriptBuilder EmitAppCall(
            this ScriptBuilder sb, 
            UInt160 scriptHash, 
            params ContractParameter[] parameters)
        {
            for (int i = parameters.Length - 1; i >= 0; i--)
            {
                sb.EmitPush(parameters[i]);
            }

            return sb.EmitAppCall(scriptHash);
        }

        public static ScriptBuilder EmitAppCall(this ScriptBuilder sb, UInt160 scriptHash, string operation)
        {
            sb.EmitPush(false);
            sb.EmitPush(operation);
            sb.EmitAppCall(scriptHash);
            return sb;
        }

        public static ScriptBuilder EmitAppCall(
            this ScriptBuilder sb, 
            UInt160 scriptHash, 
            string operation, 
            params ContractParameter[] args)
        {
            for (var i = args.Length - 1; i >= 0; i--)
            {
                sb.EmitPush(args[i]);
            }

            sb.EmitPush(args.Length);
            sb.Emit(OpCode.PACK);
            sb.EmitPush(operation);
            sb.EmitAppCall(scriptHash);
            return sb;
        }

        public static ScriptBuilder EmitAppCall(
            this ScriptBuilder sb, 
            UInt160 scriptHash, 
            string operation, 
            params object[] args)
        {
            for (var i = args.Length - 1; i >= 0; i--)
            {
                sb.EmitPush(args[i]);
            }

            sb.EmitPush(args.Length);
            sb.Emit(OpCode.PACK);
            sb.EmitPush(operation);
            sb.EmitAppCall(scriptHash);
            return sb;
        }

        public static ScriptBuilder EmitPush(this ScriptBuilder sb, ISerializable data) =>
            sb.EmitPush(data.ToArray());        

        public static ScriptBuilder EmitPush(this ScriptBuilder sb, ContractParameter parameter)
        {
            switch (parameter.Type)
            {
                case ContractParameterType.Signature:
                case ContractParameterType.ByteArray:
                    sb.EmitPush((byte[])parameter.Value);
                    break;
                case ContractParameterType.Boolean:
                    sb.EmitPush((bool)parameter.Value);
                    break;
                case ContractParameterType.Integer:
                    if (parameter.Value is BigInteger bi)
                    {
                        sb.EmitPush(bi);
                    }
                    else
                    {
                        var number = (BigInteger)typeof(BigInteger)
                            .GetConstructor(new[] { parameter.Value.GetType() })
                            .Invoke(new[] { parameter.Value });

                        sb.EmitPush(number);
                    }

                    break;
                case ContractParameterType.Hash160:
                    sb.EmitPush((UInt160)parameter.Value);
                    break;
                case ContractParameterType.Hash256:
                    sb.EmitPush((UInt256)parameter.Value);
                    break;
                case ContractParameterType.PublicKey:
                    sb.EmitPush((ECPoint)parameter.Value);
                    break;
                case ContractParameterType.String:
                    sb.EmitPush((string)parameter.Value);
                    break;
                case ContractParameterType.Array:
                    {
                        var parameters = (IList<ContractParameter>)parameter.Value;
                        for (var i = parameters.Count - 1; i >= 0; i--)
                        {
                            sb.EmitPush(parameters[i]);
                        }

                        sb.EmitPush(parameters.Count);
                        sb.Emit(OpCode.PACK);
                    }

                    break;
                default:
                    throw new ArgumentException();
            }

            return sb;
        }

        public static ScriptBuilder EmitPush(this ScriptBuilder sb, object obj)
        {
            switch (obj)
            {
                case bool data:
                    sb.EmitPush(data);
                    break;
                case byte[] data:
                    sb.EmitPush(data);
                    break;
                case string data:
                    sb.EmitPush(data);
                    break;
                case BigInteger data:
                    sb.EmitPush(data);
                    break;
                case ISerializable data:
                    sb.EmitPush(data);
                    break;
                case sbyte data:
                    sb.EmitPush(data);
                    break;
                case byte data:
                    sb.EmitPush(data);
                    break;
                case short data:
                    sb.EmitPush(data);
                    break;
                case ushort data:
                    sb.EmitPush(data);
                    break;
                case int data:
                    sb.EmitPush(data);
                    break;
                case uint data:
                    sb.EmitPush(data);
                    break;
                case long data:
                    sb.EmitPush(data);
                    break;
                case ulong data:
                    sb.EmitPush(data);
                    break;
                case Enum data:
                    sb.EmitPush(BigInteger.Parse(data.ToString("d")));
                    break;
                default:
                    throw new ArgumentException();
            }

            return sb;
        }

        public static ScriptBuilder EmitSysCall(this ScriptBuilder sb, string api, params object[] args)
        {
            for (int i = args.Length - 1; i >= 0; i--)
            {
                ScriptBuilderExtensions.EmitPush(sb, args[i]);
            }

            return sb.EmitSysCall(api);
        }
    }
}
