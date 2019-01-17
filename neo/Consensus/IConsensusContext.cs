using System;
using System.Collections.Generic;
using Neo.Cryptography.ECC;
using Neo.Network.P2P.Payloads;

namespace Neo.Consensus
{
    public interface IConsensusContext : IDisposable
    {
        ConsensusStates State { get; set; }

        UInt256 PreviousBlockHash { get; }

        uint BlockIndex { get; }

        byte ViewNumber { get; }

        ECPoint[] Validators { get; }

        int MyIndex { get; }

        uint PrimaryIndex { get; }

        uint Timestamp { get; set; }

        ulong Nonce { get; set; }

        UInt160 NextConsensus { get; set; }

        UInt256[] TransactionHashes { get; set; }

        Dictionary<UInt256, Transaction> Transactions { get; set; }

        byte[][] Signatures { get; set; }

        byte[] ExpectedView { get; set; }

        int M { get; }

        Header PrevHeader { get; }

        bool TransactionExists(UInt256 hash);

        bool VerifyTransaction(Transaction tx);

        void ChangeView(byte viewNumber);

        Block CreateBlock();

        uint GetPrimaryIndex(byte viewNumber);

        ConsensusPayload MakeChangeView();

        Block MakeHeader();

        void SignHeader();

        ConsensusPayload MakePrepareRequest();

        ConsensusPayload MakePrepareResponse(byte[] signature);

        void Reset();

        void Fill();

        bool VerifyRequest();
    }
}
