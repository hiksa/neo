using System;
using System.Collections.Generic;
using System.Linq;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.Wallets;

namespace Neo.Consensus
{
    internal class ConsensusContext : IConsensusContext
    {
        public const uint Version = 0;
        private readonly Wallet wallet;
        private Snapshot snapshot;
        private KeyPair keyPair;
        private Block header = null;

        public ConsensusContext(Wallet wallet)
        {
            this.wallet = wallet;
        }

        public ConsensusStates State { get; set; }
        public Dictionary<UInt256, Transaction> Transactions { get; set; }
        public UInt256 PreviousBlockHash { get; set; }
        public UInt256[] TransactionHashes { get; set; }
        public UInt160 NextConsensus { get; set; }
        public ECPoint[] Validators { get; set; }
        public byte[][] Signatures { get; set; }
        public byte[] ExpectedView { get; set; }
        public byte ViewNumber { get; set; }
        public ulong Nonce { get; set; }
        public uint BlockIndex { get; set; }
        public uint PrimaryIndex { get; set; }
        public uint Timestamp { get; set; }
        public int MyIndex { get; set; }

        public int M => this.Validators.Length - ((this.Validators.Length - 1) / 3);

        public Header PrevHeader => this.snapshot.GetHeader(this.PreviousBlockHash);

        public bool TransactionExists(UInt256 hash) => this.snapshot.ContainsTransaction(hash);

        public bool VerifyTransaction(Transaction tx) => tx.Verify(this.snapshot, this.Transactions.Values);

        public void ChangeView(byte viewNumber)
        {
            this.State &= ConsensusStates.SignatureSent;
            this.ViewNumber = viewNumber;
            this.PrimaryIndex = this.GetPrimaryIndex(viewNumber);
            if (this.State == ConsensusStates.Initial)
            {
                this.TransactionHashes = null;
                this.Signatures = new byte[this.Validators.Length][];
            }

            if (this.MyIndex >= 0)
            {
                this.ExpectedView[this.MyIndex] = viewNumber;
            }

            this.header = null;
        }

        public Block CreateBlock()
        {
            var block = this.MakeHeader();
            if (block == null)
            {
                return null;
            }

            var contract = Contract.CreateMultiSigContract(this.M, this.Validators);
            var parametersContext = new ContractParametersContext(block);
            for (int i = 0, j = 0; i < this.Validators.Length && j < this.M; i++)
            {
                if (this.Signatures[i] != null)
                {
                    parametersContext.AddSignature(contract, this.Validators[i], this.Signatures[i]);
                    j++;
                }
            }

            parametersContext.Verifiable.Witnesses = parametersContext.GetWitnesses();
            block.Transactions = this.TransactionHashes.Select(p => this.Transactions[p]).ToArray();
            return block;
        }

        public void Dispose() => this.snapshot?.Dispose();

        public uint GetPrimaryIndex(byte viewNumber)
        {
            var index = ((int)this.BlockIndex - viewNumber) % this.Validators.Length;
            return index >= 0 
                ? (uint)index 
                : (uint)(index + this.Validators.Length);
        }

        public ConsensusPayload MakeChangeView()
        {
            var changeViewMessage = new ChangeView { NewViewNumber = this.ExpectedView[this.MyIndex] };
            return this.MakeSignedPayload(changeViewMessage);
        }

        public Block MakeHeader()
        {
            if (this.TransactionHashes == null)
            {
                return null;
            }

            if (this.header == null)
            {
                this.header = new Block
                {
                    Version = ConsensusContext.Version,
                    PrevHash = this.PreviousBlockHash,
                    MerkleRoot = MerkleTree.ComputeRoot(this.TransactionHashes),
                    Timestamp = this.Timestamp,
                    Index = this.BlockIndex,
                    ConsensusData = this.Nonce,
                    NextConsensus = this.NextConsensus,
                    Transactions = new Transaction[0]
                };
            }

            return this.header;
        }

        public void SignHeader() =>
            this.Signatures[this.MyIndex] = this.MakeHeader()?.Sign(this.keyPair);

        public ConsensusPayload MakePrepareRequest()
        {
            var prepareRequestMessage = new PrepareRequest
            {
                Nonce = this.Nonce,
                NextConsensus = this.NextConsensus,
                TransactionHashes = this.TransactionHashes,
                MinerTransaction = (MinerTransaction)this.Transactions[this.TransactionHashes[0]],
                Signature = this.Signatures[this.MyIndex]
            };

            return this.MakeSignedPayload(prepareRequestMessage);
        }

        public ConsensusPayload MakePrepareResponse(byte[] signature)
        {
            var prepareResponse = new PrepareResponse { Signature = signature };
            return this.MakeSignedPayload(prepareResponse);
        }

        public void Reset()
        {
            this.snapshot?.Dispose();
            this.snapshot = Blockchain.Instance.GetSnapshot();
            this.State = ConsensusStates.Initial;
            this.PreviousBlockHash = this.snapshot.CurrentBlockHash;
            this.BlockIndex = this.snapshot.Height + 1;
            this.ViewNumber = 0;
            this.Validators = this.snapshot.GetValidators();
            this.MyIndex = -1;
            this.PrimaryIndex = this.BlockIndex % (uint)this.Validators.Length;
            this.TransactionHashes = null;
            this.Signatures = new byte[this.Validators.Length][];
            this.ExpectedView = new byte[this.Validators.Length];
            this.keyPair = null;

            for (int i = 0; i < this.Validators.Length; i++)
            {
                var account = this.wallet.GetAccount(this.Validators[i]);
                if (account?.HasKey == true)
                {
                    this.MyIndex = i;
                    this.keyPair = account.GetKey();
                    break;
                }
            }

            this.header = null;
        }

        public void Fill()
        {
            var mempool = Blockchain.Instance.GetMemoryPool();
            foreach (var plugin in Plugin.Policies)
            {
                mempool = plugin.FilterForBlock(mempool);
            }

            var transactions = mempool.ToList();
            var networkFee = Block.CalculateNetFee(transactions);
            var outputs = networkFee == Fixed8.Zero 
                ? new TransactionOutput[0] 
                : new TransactionOutput[1] 
                {
                    new TransactionOutput
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = networkFee,
                        ScriptHash = this.wallet.GetChangeAddress()
                    }
                };

            while (true)
            {
                var nonce = ConsensusContext.GetNonce();
                var minerTransaction = new MinerTransaction
                {
                    Nonce = (uint)(nonce % (uint.MaxValue + 1ul)),
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = outputs,
                    Witnesses = new Witness[0]
                };

                if (!this.snapshot.ContainsTransaction(minerTransaction.Hash))
                {
                    this.Nonce = nonce;
                    transactions.Insert(0, minerTransaction);
                    break;
                }
            }

            this.TransactionHashes = transactions.Select(p => p.Hash).ToArray();
            this.Transactions = transactions.ToDictionary(p => p.Hash);

            var validators = this.snapshot.GetValidators(transactions).ToArray();
            this.NextConsensus = Blockchain.GetConsensusAddress(validators);

            var currentTimestamp = TimeProvider.Current.UtcNow.ToTimestamp();
            this.Timestamp = Math.Max(currentTimestamp, this.PrevHeader.Timestamp + 1);
        }

        public bool VerifyRequest()
        {
            if (!this.State.HasFlag(ConsensusStates.RequestReceived))
            {
                return false;
            }

            var validators = this.snapshot.GetValidators(this.Transactions.Values).ToArray();
            if (!Blockchain.GetConsensusAddress(validators).Equals(this.NextConsensus))
            {
                return false;
            }

            var minerTransaction = this.Transactions
                .Values
                .FirstOrDefault(p => p.Type == TransactionType.MinerTransaction);

            var networkFeeSum = Block.CalculateNetFee(this.Transactions.Values);
            if (minerTransaction?.Outputs.Sum(p => p.Value) != networkFeeSum)
            {
                return false;
            }

            return true;
        }

        private static ulong GetNonce()
        {
            var nonce = new byte[sizeof(ulong)];
            var random = new Random();
            random.NextBytes(nonce);
            return nonce.ToUInt64(0);
        }

        private ConsensusPayload MakeSignedPayload(ConsensusMessage message)
        {
            message.ViewNumber = this.ViewNumber;
            var payload = new ConsensusPayload
            {
                Version = Version,
                PrevHash = this.PreviousBlockHash,
                BlockIndex = this.BlockIndex,
                ValidatorIndex = (ushort)this.MyIndex,
                Timestamp = this.Timestamp,
                Data = message.ToArray()
            };

            this.SignPayload(payload);
            return payload;
        }

        private void SignPayload(ConsensusPayload payload)
        {
            ContractParametersContext parametersContext;
            try
            {
                parametersContext = new ContractParametersContext(payload);
                this.wallet.Sign(parametersContext);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            parametersContext.Verifiable.Witnesses = parametersContext.GetWitnesses();
        }
    }
}
