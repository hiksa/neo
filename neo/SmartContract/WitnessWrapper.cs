using System.Linq;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;

namespace Neo.SmartContract
{
    internal class WitnessWrapper
    {
        public byte[] VerificationScript { get; private set; }

        public static WitnessWrapper[] Create(IVerifiable verifiable, Snapshot snapshot)
        {
            var witnessWrappers = verifiable
                .Witnesses
                .Select(p => new WitnessWrapper { VerificationScript = p.VerificationScript })
                .ToArray();

            if (witnessWrappers.Any(p => p.VerificationScript.Length == 0))
            {
                var hashes = verifiable.GetScriptHashesForVerifying(snapshot);
                for (int i = 0; i < witnessWrappers.Length; i++)
                {
                    if (witnessWrappers[i].VerificationScript.Length == 0)
                    {
                        witnessWrappers[i].VerificationScript = snapshot.Contracts[hashes[i]].Script;
                    }
                }
            }

            return witnessWrappers;
        }
    }
}
