using System;
using System.Collections.Generic;
using System.Linq;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.IO.Caching;
using Neo.IO.Wrappers;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;
using Neo.VM;

namespace Neo.Persistence
{
    public abstract class Snapshot : IDisposable, IPersistence, IScriptTable
    {
        private ECPoint[] validators = null;

        public Block PersistingBlock { get; internal set; }
        public abstract DataCache<UInt256, BlockState> Blocks { get; }
        public abstract DataCache<UInt256, TransactionState> Transactions { get; }
        public abstract DataCache<UInt160, AccountState> Accounts { get; }
        public abstract DataCache<UInt256, UnspentCoinState> UnspentCoins { get; }
        public abstract DataCache<UInt256, SpentCoinState> SpentCoins { get; }
        public abstract DataCache<ECPoint, ValidatorState> Validators { get; }
        public abstract DataCache<UInt256, AssetState> Assets { get; }
        public abstract DataCache<UInt160, ContractState> Contracts { get; }
        public abstract DataCache<StorageKey, StorageItem> Storages { get; }
        public abstract DataCache<UInt32Wrapper, HeaderHashList> HeaderHashList { get; }
        public abstract MetaDataCache<ValidatorsCountState> ValidatorsCount { get; }
        public abstract MetaDataCache<HashIndexState> BlockHashIndex { get; }
        public abstract MetaDataCache<HashIndexState> HeaderHashIndex { get; }

        public uint Height => this.BlockHashIndex.Get().Index;
        public uint HeaderHeight => this.HeaderHashIndex.Get().Index;
        public UInt256 CurrentBlockHash => this.BlockHashIndex.Get().Hash;
        public UInt256 CurrentHeaderHash => this.HeaderHashIndex.Get().Hash;

        public Fixed8 CalculateBonus(IEnumerable<CoinReference> inputs, bool ignoreClaimed = true)
        {
            var unclaimedCoins = new List<SpentCoin>();
            foreach (var group in inputs.GroupBy(p => p.PrevHash))
            {
                var claimableCoins = this.GetUnclaimed(group.Key);
                if (claimableCoins == null || claimableCoins.Count == 0)
                {
                    if (ignoreClaimed)
                    {
                        continue;
                    }
                    else
                    {
                        throw new ArgumentException();
                    }
                }

                foreach (var claim in group)
                {
                    if (!claimableCoins.TryGetValue(claim.PrevIndex, out SpentCoin claimed))
                    {
                        if (ignoreClaimed)
                        {
                            continue;
                        }
                        else
                        {
                            throw new ArgumentException();
                        }
                    }

                    unclaimedCoins.Add(claimed);
                }
            }

            return this.CalculateBonusInternal(unclaimedCoins);
        }

        public Fixed8 CalculateBonus(IEnumerable<CoinReference> inputs, uint heightEnd)
        {
            var unclaimedCoins = new List<SpentCoin>();
            foreach (var group in inputs.GroupBy(p => p.PrevHash))
            {
                var transactionState = this.Transactions.TryGet(group.Key);
                if (transactionState == null)
                {
                    throw new ArgumentException();
                }

                if (transactionState.BlockIndex == heightEnd)
                {
                    continue;
                }

                foreach (CoinReference claim in group)
                {
                    if (claim.PrevIndex >= transactionState.Transaction.Outputs.Length 
                        || !transactionState.Transaction.Outputs[claim.PrevIndex].AssetId.Equals(Blockchain.GoverningToken.Hash))
                    {
                        throw new ArgumentException();
                    }

                    var coin = new SpentCoin
                    {
                        Output = transactionState.Transaction.Outputs[claim.PrevIndex],
                        StartHeight = transactionState.BlockIndex,
                        EndHeight = heightEnd
                    };

                    unclaimedCoins.Add(coin);
                }
            }

            return this.CalculateBonusInternal(unclaimedCoins);
        }

        public Snapshot Clone() => new CloneSnapshot(this);

        public virtual void Commit()
        {
            this.Accounts.DeleteWhere((k, v) => !v.IsFrozen && v.Votes.Length == 0 && v.Balances.All(p => p.Value <= Fixed8.Zero));
            this.UnspentCoins.DeleteWhere((k, v) => v.Items.All(p => p.HasFlag(CoinStates.Spent)));
            this.SpentCoins.DeleteWhere((k, v) => v.Items.Count == 0);
            this.Blocks.Commit();
            this.Transactions.Commit();
            this.Accounts.Commit();
            this.UnspentCoins.Commit();
            this.SpentCoins.Commit();
            this.Validators.Commit();
            this.Assets.Commit();
            this.Contracts.Commit();
            this.Storages.Commit();
            this.HeaderHashList.Commit();
            this.ValidatorsCount.Commit();
            this.BlockHashIndex.Commit();
            this.HeaderHashIndex.Commit();
        }

        public virtual void Dispose()
        {
        }

        byte[] IScriptTable.GetScript(byte[] scriptHash) => this.Contracts[new UInt160(scriptHash)].Script;
        
        public Dictionary<ushort, SpentCoin> GetUnclaimed(UInt256 hash)
        {
            var transactionState = this.Transactions.TryGet(hash);
            if (transactionState == null)
            {
                return null;
            }

            var coinState = this.SpentCoins.TryGet(hash);
            if (coinState != null)
            {
                return coinState.Items.ToDictionary(
                    p => p.Key, 
                    p => new SpentCoin
                    {
                        Output = transactionState.Transaction.Outputs[p.Key],
                        StartHeight = transactionState.BlockIndex,
                        EndHeight = p.Value
                    });
            }
            else
            {
                return new Dictionary<ushort, SpentCoin>();
            }
        }

        public ECPoint[] GetValidators()
        {
            if (this.validators == null)
            {
                this.validators = this.GetValidators(Enumerable.Empty<Transaction>()).ToArray();
            }

            return this.validators;
        }

        public IEnumerable<ECPoint> GetValidators(IEnumerable<Transaction> others)
        {
            var snapshot = this.Clone();
            foreach (var tx in others)
            {
                foreach (var output in tx.Outputs)
                {
                    var account = snapshot.Accounts.GetAndChange(
                        output.ScriptHash, 
                        () => new AccountState(output.ScriptHash));

                    if (account.Balances.ContainsKey(output.AssetId))
                    {
                        account.Balances[output.AssetId] += output.Value;
                    }
                    else
                    {
                        account.Balances[output.AssetId] = output.Value;
                    }

                    if (output.AssetId.Equals(Blockchain.GoverningToken.Hash) && account.Votes.Length > 0)
                    {
                        foreach (var publicKey in account.Votes)
                        {
                            var validator = snapshot.Validators
                                .GetAndChange(publicKey, () => new ValidatorState(publicKey));

                            validator.Votes += output.Value;
                        }

                        var validatorsCount = snapshot.ValidatorsCount.GetAndChange();
                        validatorsCount.Votes[account.Votes.Length - 1] += output.Value;
                    }
                }

                foreach (var group in tx.Inputs.GroupBy(p => p.PrevHash))
                {
                    var previousTransaction = snapshot.GetTransaction(group.Key);
                    foreach (var input in group)
                    {
                        var previousOutput = previousTransaction.Outputs[input.PrevIndex];
                        var account = snapshot.Accounts.GetAndChange(previousOutput.ScriptHash);

                        if (previousOutput.AssetId.Equals(Blockchain.GoverningToken.Hash))
                        {
                            if (account.Votes.Length > 0)
                            {
                                foreach (var publicKey in account.Votes)
                                {
                                    var validator = snapshot.Validators.GetAndChange(publicKey);
                                    validator.Votes -= previousOutput.Value;
                                    if (!validator.Registered && validator.Votes.Equals(Fixed8.Zero))
                                    {
                                        snapshot.Validators.Delete(publicKey);
                                    }
                                }

                                var validatorsCount = snapshot.ValidatorsCount.GetAndChange();
                                validatorsCount.Votes[account.Votes.Length - 1] -= previousOutput.Value;
                            }
                        }

                        account.Balances[previousOutput.AssetId] -= previousOutput.Value;
                    }
                }

                switch (tx)
                {
#pragma warning disable CS0612
                    case EnrollmentTransaction enrollmentTransaction:
                        var publicKey = enrollmentTransaction.PublicKey;
                        var validator = snapshot.Validators.GetAndChange(publicKey, () => new ValidatorState(publicKey));
                        validator.Registered = true;
                        break;
#pragma warning restore CS0612
                    case StateTransaction stateTransaction:
                        foreach (var descriptor in stateTransaction.Descriptors)
                        {
                            switch (descriptor.Type)
                            {
                                case StateType.Account:
                                    Blockchain.ProcessAccountStateDescriptor(descriptor, snapshot);
                                    break;
                                case StateType.Validator:
                                    Blockchain.ProcessValidatorStateDescriptor(descriptor, snapshot);
                                    break;
                            }
                        }

                        break;
                }
            }

            var count = (int)snapshot
                .ValidatorsCount
                .Get()
                .Votes
                .Select((p, i) => new
                {
                    Count = i,
                    Votes = p
                })
                .Where(p => p.Votes > Fixed8.Zero)
                .ToArray()
                .WeightedFilter(
                    start: 0.25, 
                    end: 0.75, 
                    weightSelector: p => p.Votes.GetData(), 
                    resultSelector: (p, w) => new { p.Count, Weight = w })
                .WeightedAverage(p => p.Count, p => p.Weight);

            count = Math.Max(count, Blockchain.StandbyValidators.Length);

            var standbyValidators = new HashSet<ECPoint>(Blockchain.StandbyValidators);
            var publicKeys = snapshot.Validators
                .Find()
                .Select(p => p.Value)
                .Where(p => (p.Registered && p.Votes > Fixed8.Zero) || standbyValidators.Contains(p.PublicKey))
                .OrderByDescending(p => p.Votes)
                .ThenBy(p => p.PublicKey)
                .Select(p => p.PublicKey)
                .Take(count)
                .ToArray();

            IEnumerable<ECPoint> result;
            if (publicKeys.Length == count)
            {
                result = publicKeys;
            }
            else
            {
                var hashSet = new HashSet<ECPoint>(publicKeys);
                for (int i = 0; i < Blockchain.StandbyValidators.Length && hashSet.Count < count; i++)
                {
                    hashSet.Add(Blockchain.StandbyValidators[i]);
                }

                result = hashSet;
            }

            return result.OrderBy(p => p);
        }

        private Fixed8 CalculateBonusInternal(IEnumerable<SpentCoin> unclaimed)
        {
            var claimedAmount = Fixed8.Zero;
            foreach (var group in unclaimed.GroupBy(p => new { p.StartHeight, p.EndHeight }))
            {
                uint amount = 0;
                uint ustart = group.Key.StartHeight / Blockchain.DecrementInterval;
                if (ustart < Blockchain.GenerationAmount.Length)
                {
                    uint istart = group.Key.StartHeight % Blockchain.DecrementInterval;
                    uint uend = group.Key.EndHeight / Blockchain.DecrementInterval;
                    uint iend = group.Key.EndHeight % Blockchain.DecrementInterval;
                    if (uend >= Blockchain.GenerationAmount.Length)
                    {
                        uend = (uint)Blockchain.GenerationAmount.Length;
                        iend = 0;
                    }

                    if (iend == 0)
                    {
                        uend--;
                        iend = Blockchain.DecrementInterval;
                    }

                    while (ustart < uend)
                    {
                        amount += (Blockchain.DecrementInterval - istart) * Blockchain.GenerationAmount[ustart];
                        ustart++;
                        istart = 0;
                    }

                    amount += (iend - istart) * Blockchain.GenerationAmount[ustart];
                }

                var startingSystemFee = group.Key.StartHeight == 0 
                    ? 0 
                    : this.GetSysFeeAmount(group.Key.StartHeight - 1);
                var endingSystemFee = this.GetSysFeeAmount(group.Key.EndHeight - 1);

                amount += (uint)(endingSystemFee - startingSystemFee);
                claimedAmount += group.Sum(p => p.Value) / 100000000 * amount;
            }

            return claimedAmount;
        }
    }
}
