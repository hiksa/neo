using System;
using System.Reflection;
using Neo.Cryptography.ECC;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using Neo.IO.Wrappers;
using Neo.Ledger;
using Neo.Ledger.States;

namespace Neo.Persistence.LevelDB
{
    public class LevelDBStore : Store, IDisposable
    {
        private readonly DB db;

        public LevelDBStore(string path)
        {
            this.db = DB.Open(path, new Options { CreateIfMissing = true });

            if (this.db.TryGet(ReadOptions.Default, SliceBuilder.Begin(Prefixes.SYSVersion), out Slice value) 
                && Version.TryParse(value.ToString(), out Version version) 
                && version >= Version.Parse("2.9.1"))
            {
                return;
            }

            var batch = new WriteBatch();
            var readOptions = new ReadOptions { FillCache = false };
            using (var iterator = this.db.NewIterator(readOptions))
            {
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    batch.Delete(iterator.Key());
                }
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            this.db.Put(WriteOptions.Default, SliceBuilder.Begin(Prefixes.SYSVersion), assemblyVersion);
            this.db.Write(WriteOptions.Default, batch);
        }

        public void Dispose() => this.db.Dispose();

        public override DataCache<UInt160, AccountState> GetAccounts() =>
            new DbCache<UInt160, AccountState>(this.db, null, null, Prefixes.STAccount);
        
        public override DataCache<UInt256, AssetState> GetAssets() =>
            new DbCache<UInt256, AssetState>(this.db, null, null, Prefixes.STAsset);
        
        public override DataCache<UInt256, BlockState> GetBlocks() =>
            new DbCache<UInt256, BlockState>(this.db, null, null, Prefixes.DataBlock);
        
        public override DataCache<UInt160, ContractState> GetContracts() =>
            new DbCache<UInt160, ContractState>(this.db, null, null, Prefixes.STContract);
        
        public override Snapshot GetSnapshot() => new DbSnapshot(this.db);

        public override DataCache<UInt256, SpentCoinState> GetSpentCoins() =>
            new DbCache<UInt256, SpentCoinState>(this.db, null, null, Prefixes.STSpentCoin);

        public override DataCache<StorageKey, StorageItem> GetStorages() =>
            new DbCache<StorageKey, StorageItem>(this.db, null, null, Prefixes.STStorage);
        
        public override DataCache<UInt256, TransactionState> GetTransactions() =>
            new DbCache<UInt256, TransactionState>(this.db, null, null, Prefixes.DataTransaction);
        
        public override DataCache<UInt256, UnspentCoinState> GetUnspentCoins() =>
            new DbCache<UInt256, UnspentCoinState>(this.db, null, null, Prefixes.STCoin);

        public override DataCache<ECPoint, ValidatorState> GetValidators() =>
            new DbCache<ECPoint, ValidatorState>(this.db, null, null, Prefixes.STValidator);
        
        public override DataCache<UInt32Wrapper, HeaderHashList> GetHeaderHashList() =>
            new DbCache<UInt32Wrapper, HeaderHashList>(this.db, null, null, Prefixes.IXHeaderHashList);
        
        public override MetaDataCache<ValidatorsCountState> GetValidatorsCount() =>
            new DbMetaDataCache<ValidatorsCountState>(this.db, null, null, Prefixes.IXValidatorsCount);
        
        public override MetaDataCache<HashIndexState> GetBlockHashIndex() =>
            new DbMetaDataCache<HashIndexState>(this.db, null, null, Prefixes.IXCurrentBlock);
        
        public override MetaDataCache<HashIndexState> GetHeaderHashIndex() =>
            new DbMetaDataCache<HashIndexState>(this.db, null, null, Prefixes.IXCurrentHeader);
    }
}
