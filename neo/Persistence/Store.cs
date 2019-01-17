using Neo.Cryptography.ECC;
using Neo.IO.Caching;
using Neo.IO.Wrappers;
using Neo.Ledger;
using Neo.Ledger.States;

namespace Neo.Persistence
{
    public abstract class Store : IPersistence
    {
        DataCache<UInt256, BlockState> IPersistence.Blocks => this.GetBlocks();
        DataCache<UInt256, TransactionState> IPersistence.Transactions => this.GetTransactions();
        DataCache<UInt160, AccountState> IPersistence.Accounts => this.GetAccounts();
        DataCache<UInt256, UnspentCoinState> IPersistence.UnspentCoins => this.GetUnspentCoins();
        DataCache<UInt256, SpentCoinState> IPersistence.SpentCoins => this.GetSpentCoins();
        DataCache<ECPoint, ValidatorState> IPersistence.Validators => this.GetValidators();
        DataCache<UInt256, AssetState> IPersistence.Assets => this.GetAssets();
        DataCache<UInt160, ContractState> IPersistence.Contracts => this.GetContracts();
        DataCache<StorageKey, StorageItem> IPersistence.Storages => this.GetStorages();
        DataCache<UInt32Wrapper, HeaderHashList> IPersistence.HeaderHashList => this.GetHeaderHashList();
        MetaDataCache<ValidatorsCountState> IPersistence.ValidatorsCount => this.GetValidatorsCount();
        MetaDataCache<HashIndexState> IPersistence.BlockHashIndex => this.GetBlockHashIndex();
        MetaDataCache<HashIndexState> IPersistence.HeaderHashIndex => this.GetHeaderHashIndex();

        public abstract DataCache<UInt256, BlockState> GetBlocks();
        public abstract DataCache<UInt256, TransactionState> GetTransactions();
        public abstract DataCache<UInt160, AccountState> GetAccounts();
        public abstract DataCache<UInt256, UnspentCoinState> GetUnspentCoins();
        public abstract DataCache<UInt256, SpentCoinState> GetSpentCoins();
        public abstract DataCache<ECPoint, ValidatorState> GetValidators();
        public abstract DataCache<UInt256, AssetState> GetAssets();
        public abstract DataCache<UInt160, ContractState> GetContracts();
        public abstract DataCache<StorageKey, StorageItem> GetStorages();
        public abstract DataCache<UInt32Wrapper, HeaderHashList> GetHeaderHashList();
        public abstract MetaDataCache<ValidatorsCountState> GetValidatorsCount();
        public abstract MetaDataCache<HashIndexState> GetBlockHashIndex();
        public abstract MetaDataCache<HashIndexState> GetHeaderHashIndex();

        public abstract Snapshot GetSnapshot();
    }
}
