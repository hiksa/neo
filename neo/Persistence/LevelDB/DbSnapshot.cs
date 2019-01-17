using Neo.Cryptography.ECC;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using Neo.IO.Wrappers;
using Neo.Ledger;
using Neo.Ledger.States;
using LSnapshot = Neo.IO.Data.LevelDB.Snapshot;

namespace Neo.Persistence.LevelDB
{
    internal class DbSnapshot : Snapshot
    {
        private readonly DB db;
        private readonly LSnapshot snapshot;
        private readonly WriteBatch batch;

        public DbSnapshot(DB db)
        {
            this.db = db;
            this.snapshot = this.db.GetSnapshot();
            this.batch = new WriteBatch();

            var options = new ReadOptions { FillCache = false, Snapshot = this.snapshot };

            this.Blocks = new DbCache<UInt256, BlockState>(this.db, options, this.batch, Prefixes.DataBlock);
            this.Transactions = new DbCache<UInt256, TransactionState>(this.db, options, this.batch, Prefixes.DataTransaction);
            this.Accounts = new DbCache<UInt160, AccountState>(this.db, options, this.batch, Prefixes.STAccount);
            this.UnspentCoins = new DbCache<UInt256, UnspentCoinState>(this.db, options, this.batch, Prefixes.STCoin);
            this.SpentCoins = new DbCache<UInt256, SpentCoinState>(this.db, options, this.batch, Prefixes.STSpentCoin);
            this.Validators = new DbCache<ECPoint, ValidatorState>(this.db, options, this.batch, Prefixes.STValidator);
            this.Assets = new DbCache<UInt256, AssetState>(this.db, options, this.batch, Prefixes.STAsset);
            this.Contracts = new DbCache<UInt160, ContractState>(this.db, options, this.batch, Prefixes.STContract);
            this.Storages = new DbCache<StorageKey, StorageItem>(this.db, options, this.batch, Prefixes.STStorage);
            this.HeaderHashList = new DbCache<UInt32Wrapper, HeaderHashList>(this.db, options, this.batch, Prefixes.IXHeaderHashList);
            this.ValidatorsCount = new DbMetaDataCache<ValidatorsCountState>(this.db, options, this.batch, Prefixes.IXValidatorsCount);
            this.BlockHashIndex = new DbMetaDataCache<HashIndexState>(this.db, options, this.batch, Prefixes.IXCurrentBlock);
            this.HeaderHashIndex = new DbMetaDataCache<HashIndexState>(this.db, options, this.batch, Prefixes.IXCurrentHeader);
        }

        public override DataCache<UInt256, BlockState> Blocks { get; }
        public override DataCache<UInt256, TransactionState> Transactions { get; }
        public override DataCache<UInt160, AccountState> Accounts { get; }
        public override DataCache<UInt256, UnspentCoinState> UnspentCoins { get; }
        public override DataCache<UInt256, SpentCoinState> SpentCoins { get; }
        public override DataCache<ECPoint, ValidatorState> Validators { get; }
        public override DataCache<UInt256, AssetState> Assets { get; }
        public override DataCache<UInt160, ContractState> Contracts { get; }
        public override DataCache<StorageKey, StorageItem> Storages { get; }
        public override DataCache<UInt32Wrapper, HeaderHashList> HeaderHashList { get; }
        public override MetaDataCache<ValidatorsCountState> ValidatorsCount { get; }
        public override MetaDataCache<HashIndexState> BlockHashIndex { get; }
        public override MetaDataCache<HashIndexState> HeaderHashIndex { get; }

        public override void Commit()
        {
            base.Commit();
            this.db.Write(WriteOptions.Default, this.batch);
        }

        public override void Dispose() => this.snapshot.Dispose();
    }
}
