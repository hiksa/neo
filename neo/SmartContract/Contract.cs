using System;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.VM;

namespace Neo.SmartContract
{
    public class Contract
    {
        private string address;
        private UInt160 scriptHash;

        public byte[] Script { get; set; }

        public ContractParameterType[] ParameterList { get; set; }

        public string Address
        {
            get
            {
                if (this.address == null)
                {
                    this.address = this.ScriptHash.ToAddress();
                }

                return this.address;
            }
        }

        public virtual UInt160 ScriptHash
        {
            get
            {
                if (this.scriptHash == null)
                {
                    this.scriptHash = this.Script.ToScriptHash();
                }

                return this.scriptHash;
            }
        }

        public static Contract Create(ContractParameterType[] parameterList, byte[] redeemScript) =>
            new Contract { Script = redeemScript, ParameterList = parameterList };

        public static Contract CreateMultiSigContract(int m, params ECPoint[] publicKeys) =>
            new Contract
            {
                Script = Contract.CreateMultiSigRedeemScript(m, publicKeys),
                ParameterList = Enumerable.Repeat(ContractParameterType.Signature, m).ToArray()
            };

        public static byte[] CreateMultiSigRedeemScript(int m, params ECPoint[] publicKeys)
        {
            if (!(m >= 1 && m <= publicKeys.Length && publicKeys.Length <= 1024))
            {
                throw new ArgumentException();
            }

            using (var sb = new ScriptBuilder())
            {
                sb.EmitPush(m);
                foreach (var publicKey in publicKeys.OrderBy(p => p))
                {
                    sb.EmitPush(publicKey.EncodePoint(true));
                }

                sb.EmitPush(publicKeys.Length);
                sb.Emit(OpCode.CHECKMULTISIG);
                return sb.ToArray();
            }
        }

        public static Contract CreateSignatureContract(ECPoint publicKey) =>
            new Contract
            {
                Script = Contract.CreateSignatureRedeemScript(publicKey),
                ParameterList = new[] { ContractParameterType.Signature }
            };

        public static byte[] CreateSignatureRedeemScript(ECPoint publicKey)
        {
            using (var sb = new ScriptBuilder())
            {
                sb.EmitPush(publicKey.EncodePoint(true));
                sb.Emit(OpCode.CHECKSIG);
                return sb.ToArray();
            }
        }
    }
}
