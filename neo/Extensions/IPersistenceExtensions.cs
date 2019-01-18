using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.Extensions
{
    public static class IPersistenceExtensions
    {
        public static bool ContainsBlock(this IPersistence persistence, UInt256 hash)
        {
            var blockState = persistence.Blocks.TryGet(hash);
            if (blockState == null)
            {
                return false;
            }

            return blockState.TrimmedBlock.IsBlock;
        }

        public static bool ContainsTransaction(this IPersistence persistence, UInt256 hash)
        {
            var transactionState = persistence.Transactions.TryGet(hash);
            return transactionState != null;
        }

        public static Block GetBlock(this IPersistence persistence, uint index)
        {
            var hash = Blockchain.Instance.GetBlockHash(index);
            if (hash == null)
            {
                return null;
            }

            return persistence.GetBlock(hash);
        }

        public static Block GetBlock(this IPersistence persistence, UInt256 hash)
        {
            var state = persistence.Blocks.TryGet(hash);
            if (state == null)
            {
                return null;
            }

            if (!state.TrimmedBlock.IsBlock)
            {
                return null;
            }

            return state.TrimmedBlock.GetBlock(persistence.Transactions);
        }

        public static IEnumerable<ValidatorState> GetEnrollments(this IPersistence persistence)
        {
            var standbyValidators = new HashSet<ECPoint>(Blockchain.StandbyValidators);
            return persistence
                .Validators
                .Find()
                .Select(p => p.Value)
                .Where(p => p.Registered || standbyValidators.Contains(p.PublicKey));
        }

        public static Header GetHeader(this IPersistence persistence, uint index)
        {
            var hash = Blockchain.Instance.GetBlockHash(index);
            if (hash == null)
            {
                return null;
            }

            return persistence.GetHeader(hash);
        }

        public static Header GetHeader(this IPersistence persistence, UInt256 hash) =>
            persistence.Blocks.TryGet(hash)?.TrimmedBlock.Header;

        public static UInt256 GetNextBlockHash(this IPersistence persistence, UInt256 hash)
        {
            var blockState = persistence.Blocks.TryGet(hash);
            if (blockState == null)
            {
                return null;
            }

            return Blockchain.Instance.GetBlockHash(blockState.TrimmedBlock.Index + 1);
        }

        public static long GetSysFeeAmount(this IPersistence persistence, uint height) =>
            persistence.GetSysFeeAmount(Blockchain.Instance.GetBlockHash(height));

        public static long GetSysFeeAmount(this IPersistence persistence, UInt256 hash)
        {
            var blockState = persistence.Blocks.TryGet(hash);
            if (blockState == null)
            {
                return 0;
            }

            return blockState.SystemFeeAmount;
        }

        public static Transaction GetTransaction(this IPersistence persistence, UInt256 hash) =>
            persistence.Transactions.TryGet(hash)?.Transaction;

        public static TransactionOutput GetUnspent(this IPersistence persistence, UInt256 hash, ushort index)
        {
            var state = persistence.UnspentCoins.TryGet(hash);
            if (state == null)
            {
                return null;
            }

            if (index >= state.Items.Length)
            {
                return null;
            }

            if (state.Items[index].HasFlag(CoinStates.Spent))
            {
                return null;
            }

            return persistence.GetTransaction(hash).Outputs[index];
        }

        public static IEnumerable<TransactionOutput> GetUnspent(this IPersistence persistence, UInt256 hash)
        {
            var outputs = new List<TransactionOutput>();
            var unspentCoin = persistence.UnspentCoins.TryGet(hash);
            if (unspentCoin != null)
            {
                var tx = persistence.GetTransaction(hash);
                for (int i = 0; i < unspentCoin.Items.Length; i++)
                {
                    if (!unspentCoin.Items[i].HasFlag(CoinStates.Spent))
                    {
                        outputs.Add(tx.Outputs[i]);
                    }
                }
            }

            return outputs;
        }

        public static bool IsDoubleSpend(this IPersistence persistence, Transaction tx)
        {
            if (tx.Inputs.Length == 0)
            {
                return false;
            }

            foreach (var group in tx.Inputs.GroupBy(p => p.PrevHash))
            {
                var state = persistence.UnspentCoins.TryGet(group.Key);
                if (state == null)
                {
                    return true;
                }

                if (group.Any(p => p.PrevIndex >= state.Items.Length
                    || state.Items[p.PrevIndex].HasFlag(CoinStates.Spent)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
