using System;
using System.IO;
using System.Linq;
using Neo.Cryptography;
using Neo.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;
using Neo.Wallets;

namespace Neo.Extensions
{
    public static class IVerifiableExtensions
    {
        public static byte[] GetHashData(this IVerifiable verifiable)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                verifiable.SerializeUnsigned(writer);
                writer.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] Sign(this IVerifiable verifiable, KeyPair key)
        {
            var data = verifiable.GetHashData();
            var signature = key.PublicKey
                .EncodePoint(false)
                .Skip(1)
                .ToArray();

            return Crypto.Default.Sign(data, key.PrivateKey, signature);
        }

        public static bool VerifyWitnesses(this IVerifiable verifiable, Snapshot snapshot)
        {
            UInt160[] hashes;
            try
            {
                hashes = verifiable.GetScriptHashesForVerifying(snapshot);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (hashes.Length != verifiable.Witnesses.Length)
            {
                return false;
            }

            for (int i = 0; i < hashes.Length; i++)
            {
                var verification = verifiable.Witnesses[i].VerificationScript;
                if (verification.Length == 0)
                {
                    using (var sb = new ScriptBuilder())
                    {
                        sb.EmitAppCall(hashes[i].ToArray());
                        verification = sb.ToArray();
                    }
                }
                else
                {
                    if (hashes[i] != verifiable.Witnesses[i].ScriptHash)
                    {
                        return false;
                    }
                }

                using (var engine = new ApplicationEngine(TriggerType.Verification, verifiable, snapshot, Fixed8.Zero))
                {
                    engine.LoadScript(verification);
                    engine.LoadScript(verifiable.Witnesses[i].InvocationScript);
                    if (!engine.Execute())
                    {
                        return false;
                    }

                    if (engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean())
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
