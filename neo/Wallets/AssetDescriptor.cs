using System;
using Neo.Extensions;
using Neo.Ledger;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.Wallets
{
    public class AssetDescriptor
    {
        public AssetDescriptor(UIntBase assetId)
        {
            if (assetId is UInt160 assetId160)
            {
                byte[] script;
                using (var sb = new ScriptBuilder())
                {
                    sb.EmitAppCall(assetId160, "decimals");
                    sb.EmitAppCall(assetId160, "name");
                    script = sb.ToArray();
                }

                var engine = ApplicationEngine.Run(script);
                if (engine.State.HasFlag(VMState.FAULT))
                {
                    throw new ArgumentException();
                }

                this.AssetId = assetId;
                this.AssetName = engine.ResultStack.Pop().GetString();
                this.Decimals = (byte)engine.ResultStack.Pop().GetBigInteger();
            }
            else
            {
                var state = Blockchain.Instance.Store.GetAssets()[(UInt256)assetId];

                this.AssetId = state.AssetId;
                this.AssetName = state.GetName();
                this.Decimals = state.Precision;
            }
        }

        public UIntBase AssetId { get; private set; }

        public string AssetName { get; private set; }

        public byte Decimals { get; private set; }

        public override string ToString() => this.AssetName;
    }
}
