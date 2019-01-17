using System;
using System.IO;
using System.Linq;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Enumerators;
using Neo.SmartContract.Iterators;
using Neo.VM;
using Neo.VM.Types;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract
{
    public class NeoService : StandardService
    {
        public NeoService(TriggerType trigger, Snapshot snapshot)
            : base(trigger, snapshot)
        {
            this.Register("Neo.Runtime.GetTrigger", this.Runtime_GetTrigger, 1);
            this.Register("Neo.Runtime.CheckWitness", this.Runtime_CheckWitness, 200);
            this.Register("Neo.Runtime.Notify", this.Runtime_Notify, 1);
            this.Register("Neo.Runtime.Log", this.Runtime_Log, 1);
            this.Register("Neo.Runtime.GetTime", this.Runtime_GetTime, 1);
            this.Register("Neo.Runtime.Serialize", this.Runtime_Serialize, 1);
            this.Register("Neo.Runtime.Deserialize", this.Runtime_Deserialize, 1);
            this.Register("Neo.Blockchain.GetHeight", this.Blockchain_GetHeight, 1);
            this.Register("Neo.Blockchain.GetHeader", this.Blockchain_GetHeader, 100);
            this.Register("Neo.Blockchain.GetBlock", this.Blockchain_GetBlock, 200);
            this.Register("Neo.Blockchain.GetTransaction", this.Blockchain_GetTransaction, 100);
            this.Register("Neo.Blockchain.GetTransactionHeight", this.Blockchain_GetTransactionHeight, 100);
            this.Register("Neo.Blockchain.GetAccount", this.Blockchain_GetAccount, 100);
            this.Register("Neo.Blockchain.GetValidators", this.Blockchain_GetValidators, 200);
            this.Register("Neo.Blockchain.GetAsset", this.Blockchain_GetAsset, 100);
            this.Register("Neo.Blockchain.GetContract", this.Blockchain_GetContract, 100);
            this.Register("Neo.Header.GetHash", this.Header_GetHash, 1);
            this.Register("Neo.Header.GetVersion", this.Header_GetVersion, 1);
            this.Register("Neo.Header.GetPrevHash", this.Header_GetPrevHash, 1);
            this.Register("Neo.Header.GetMerkleRoot", this.Header_GetMerkleRoot, 1);
            this.Register("Neo.Header.GetTimestamp", this.Header_GetTimestamp, 1);
            this.Register("Neo.Header.GetIndex", this.Header_GetIndex, 1);
            this.Register("Neo.Header.GetConsensusData", this.Header_GetConsensusData, 1);
            this.Register("Neo.Header.GetNextConsensus", this.Header_GetNextConsensus, 1);
            this.Register("Neo.Block.GetTransactionCount", this.Block_GetTransactionCount, 1);
            this.Register("Neo.Block.GetTransactions", this.Block_GetTransactions, 1);
            this.Register("Neo.Block.GetTransaction", this.Block_GetTransaction, 1);
            this.Register("Neo.Transaction.GetHash", this.Transaction_GetHash, 1);
            this.Register("Neo.Transaction.GetType", this.Transaction_GetType, 1);
            this.Register("Neo.Transaction.GetAttributes", this.Transaction_GetAttributes, 1);
            this.Register("Neo.Transaction.GetInputs", this.Transaction_GetInputs, 1);
            this.Register("Neo.Transaction.GetOutputs", this.Transaction_GetOutputs, 1);
            this.Register("Neo.Transaction.GetReferences", this.Transaction_GetReferences, 200);
            this.Register("Neo.Transaction.GetUnspentCoins", this.Transaction_GetUnspentCoins, 200);
            this.Register("Neo.Transaction.GetWitnesses", this.Transaction_GetWitnesses, 200);
            this.Register("Neo.InvocationTransaction.GetScript", this.InvocationTransaction_GetScript, 1);
            this.Register("Neo.Witness.GetVerificationScript", this.Witness_GetVerificationScript, 100);
            this.Register("Neo.Attribute.GetUsage", this.Attribute_GetUsage, 1);
            this.Register("Neo.Attribute.GetData", this.Attribute_GetData, 1);
            this.Register("Neo.Input.GetHash", this.Input_GetHash, 1);
            this.Register("Neo.Input.GetIndex", this.Input_GetIndex, 1);
            this.Register("Neo.Output.GetAssetId", this.Output_GetAssetId, 1);
            this.Register("Neo.Output.GetValue", this.Output_GetValue, 1);
            this.Register("Neo.Output.GetScriptHash", this.Output_GetScriptHash, 1);
            this.Register("Neo.Account.GetScriptHash", this.Account_GetScriptHash, 1);
            this.Register("Neo.Account.GetVotes", this.Account_GetVotes, 1);
            this.Register("Neo.Account.GetBalance", this.Account_GetBalance, 1);
            this.Register("Neo.Account.IsStandard", this.Account_IsStandard, 100);
            this.Register("Neo.Asset.Create", this.Asset_Create);
            this.Register("Neo.Asset.Renew", this.Asset_Renew);
            this.Register("Neo.Asset.GetAssetId", this.Asset_GetAssetId, 1);
            this.Register("Neo.Asset.GetAssetType", this.Asset_GetAssetType, 1);
            this.Register("Neo.Asset.GetAmount", this.Asset_GetAmount, 1);
            this.Register("Neo.Asset.GetAvailable", this.Asset_GetAvailable, 1);
            this.Register("Neo.Asset.GetPrecision", this.Asset_GetPrecision, 1);
            this.Register("Neo.Asset.GetOwner", this.Asset_GetOwner, 1);
            this.Register("Neo.Asset.GetAdmin", this.Asset_GetAdmin, 1);
            this.Register("Neo.Asset.GetIssuer", this.Asset_GetIssuer, 1);
            this.Register("Neo.Contract.Create", this.Contract_Create);
            this.Register("Neo.Contract.Migrate", this.Contract_Migrate);
            this.Register("Neo.Contract.Destroy", this.Contract_Destroy, 1);
            this.Register("Neo.Contract.GetScript", this.Contract_GetScript, 1);
            this.Register("Neo.Contract.IsPayable", this.Contract_IsPayable, 1);
            this.Register("Neo.Contract.GetStorageContext", this.Contract_GetStorageContext, 1);
            this.Register("Neo.Storage.GetContext", this.Storage_GetContext, 1);
            this.Register("Neo.Storage.GetReadOnlyContext", this.Storage_GetReadOnlyContext, 1);
            this.Register("Neo.Storage.Get", this.Storage_Get, 100);
            this.Register("Neo.Storage.Put", this.Storage_Put);
            this.Register("Neo.Storage.Delete", this.Storage_Delete, 100);
            this.Register("Neo.Storage.Find", this.Storage_Find, 1);
            this.Register("Neo.StorageContext.AsReadOnly", this.StorageContext_AsReadOnly, 1);
            this.Register("Neo.Enumerator.Create", this.Enumerator_Create, 1);
            this.Register("Neo.Enumerator.Next", this.Enumerator_Next, 1);
            this.Register("Neo.Enumerator.Value", this.Enumerator_Value, 1);
            this.Register("Neo.Enumerator.Concat", this.Enumerator_Concat, 1);
            this.Register("Neo.Iterator.Create", this.Iterator_Create, 1);
            this.Register("Neo.Iterator.Key", this.Iterator_Key, 1);
            this.Register("Neo.Iterator.Keys", this.Iterator_Keys, 1);
            this.Register("Neo.Iterator.Values", this.Iterator_Values, 1);

            this.Register("Neo.Iterator.Next", this.Enumerator_Next, 1);
            this.Register("Neo.Iterator.Value", this.Enumerator_Value, 1);

            this.Register("AntShares.Runtime.CheckWitness", this.Runtime_CheckWitness, 200);
            this.Register("AntShares.Runtime.Notify", this.Runtime_Notify, 1);
            this.Register("AntShares.Runtime.Log", this.Runtime_Log, 1);
            this.Register("AntShares.Blockchain.GetHeight", this.Blockchain_GetHeight, 1);
            this.Register("AntShares.Blockchain.GetHeader", this.Blockchain_GetHeader, 100);
            this.Register("AntShares.Blockchain.GetBlock", this.Blockchain_GetBlock, 200);
            this.Register("AntShares.Blockchain.GetTransaction", this.Blockchain_GetTransaction, 100);
            this.Register("AntShares.Blockchain.GetAccount", this.Blockchain_GetAccount, 100);
            this.Register("AntShares.Blockchain.GetValidators", this.Blockchain_GetValidators, 200);
            this.Register("AntShares.Blockchain.GetAsset", this.Blockchain_GetAsset, 100);
            this.Register("AntShares.Blockchain.GetContract", this.Blockchain_GetContract, 100);
            this.Register("AntShares.Header.GetHash", this.Header_GetHash, 1);
            this.Register("AntShares.Header.GetVersion", this.Header_GetVersion, 1);
            this.Register("AntShares.Header.GetPrevHash", this.Header_GetPrevHash, 1);
            this.Register("AntShares.Header.GetMerkleRoot", this.Header_GetMerkleRoot, 1);
            this.Register("AntShares.Header.GetTimestamp", this.Header_GetTimestamp, 1);
            this.Register("AntShares.Header.GetConsensusData", this.Header_GetConsensusData, 1);
            this.Register("AntShares.Header.GetNextConsensus", this.Header_GetNextConsensus, 1);
            this.Register("AntShares.Block.GetTransactionCount", this.Block_GetTransactionCount, 1);
            this.Register("AntShares.Block.GetTransactions", this.Block_GetTransactions, 1);
            this.Register("AntShares.Block.GetTransaction", this.Block_GetTransaction, 1);
            this.Register("AntShares.Transaction.GetHash", this.Transaction_GetHash, 1);
            this.Register("AntShares.Transaction.GetType", this.Transaction_GetType, 1);
            this.Register("AntShares.Transaction.GetAttributes", this.Transaction_GetAttributes, 1);
            this.Register("AntShares.Transaction.GetInputs", this.Transaction_GetInputs, 1);
            this.Register("AntShares.Transaction.GetOutputs", this.Transaction_GetOutputs, 1);
            this.Register("AntShares.Transaction.GetReferences", this.Transaction_GetReferences, 200);
            this.Register("AntShares.Attribute.GetUsage", this.Attribute_GetUsage, 1);
            this.Register("AntShares.Attribute.GetData", this.Attribute_GetData, 1);
            this.Register("AntShares.Input.GetHash", this.Input_GetHash, 1);
            this.Register("AntShares.Input.GetIndex", this.Input_GetIndex, 1);
            this.Register("AntShares.Output.GetAssetId", this.Output_GetAssetId, 1);
            this.Register("AntShares.Output.GetValue", this.Output_GetValue, 1);
            this.Register("AntShares.Output.GetScriptHash", this.Output_GetScriptHash, 1);
            this.Register("AntShares.Account.GetScriptHash", this.Account_GetScriptHash, 1);
            this.Register("AntShares.Account.GetVotes", this.Account_GetVotes, 1);
            this.Register("AntShares.Account.GetBalance", this.Account_GetBalance, 1);
            this.Register("AntShares.Asset.Create", this.Asset_Create);
            this.Register("AntShares.Asset.Renew", this.Asset_Renew);
            this.Register("AntShares.Asset.GetAssetId", this.Asset_GetAssetId, 1);
            this.Register("AntShares.Asset.GetAssetType", this.Asset_GetAssetType, 1);
            this.Register("AntShares.Asset.GetAmount", this.Asset_GetAmount, 1);
            this.Register("AntShares.Asset.GetAvailable", this.Asset_GetAvailable, 1);
            this.Register("AntShares.Asset.GetPrecision", this.Asset_GetPrecision, 1);
            this.Register("AntShares.Asset.GetOwner", this.Asset_GetOwner, 1);
            this.Register("AntShares.Asset.GetAdmin", this.Asset_GetAdmin, 1);
            this.Register("AntShares.Asset.GetIssuer", this.Asset_GetIssuer, 1);
            this.Register("AntShares.Contract.Create", this.Contract_Create);
            this.Register("AntShares.Contract.Migrate", this.Contract_Migrate);
            this.Register("AntShares.Contract.Destroy", this.Contract_Destroy, 1);
            this.Register("AntShares.Contract.GetScript", this.Contract_GetScript, 1);
            this.Register("AntShares.Contract.GetStorageContext", this.Contract_GetStorageContext, 1);
            this.Register("AntShares.Storage.GetContext", this.Storage_GetContext, 1);
            this.Register("AntShares.Storage.Get", this.Storage_Get, 100);
            this.Register("AntShares.Storage.Put", this.Storage_Put);
            this.Register("AntShares.Storage.Delete", this.Storage_Delete, 100);
        }

        private bool Blockchain_GetAccount(ExecutionEngine engine)
        {
            var hashBytes = engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetByteArray();

            var hash = new UInt160(hashBytes);
            var accountState = this.Snapshot
                .Accounts
                .GetOrAdd(hash, () => new AccountState(hash));

            var account = StackItem.FromInterface(accountState);
            engine.CurrentContext.EvaluationStack.Push(account);
            return true;
        }

        private bool Blockchain_GetValidators(ExecutionEngine engine)
        {
            var validators = this.Snapshot
                .GetValidators()
                .Select(p => (StackItem)p.EncodePoint(true))
                .ToArray();

            engine.CurrentContext.EvaluationStack.Push(validators);
            return true;
        }

        private bool Blockchain_GetAsset(ExecutionEngine engine)
        {
            var hash = new UInt256(engine.CurrentContext.EvaluationStack.Pop().GetByteArray());
            var assetState = this.Snapshot.Assets.TryGet(hash);
            if (assetState == null)
            {
                return false;
            }

            var asset = StackItem.FromInterface(assetState);
            engine.CurrentContext.EvaluationStack.Push(asset);
            return true;
        }

        private bool Header_GetVersion(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.Version);
                return true;
            }

            return false;
        }

        private bool Header_GetMerkleRoot(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                var merkleRoot = header.MerkleRoot.ToArray();
                engine.CurrentContext.EvaluationStack.Push(merkleRoot);
                return true;
            }

            return false;
        }

        private bool Header_GetConsensusData(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(header.ConsensusData);
                return true;
            }

            return false;
        }

        private bool Header_GetNextConsensus(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var header = interopInterface.GetInterface<BlockBase>();
                if (header == null)
                {
                    return false;
                }

                var nextConsensus = header.NextConsensus.ToArray();
                engine.CurrentContext.EvaluationStack.Push(nextConsensus);
                return true;
            }

            return false;
        }

        private bool Transaction_GetType(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var transaction = interopInterface.GetInterface<Transaction>();
                if (transaction == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push((int)transaction.Type);
                return true;
            }

            return false;
        }

        private bool Transaction_GetAttributes(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var tx = interopInterface.GetInterface<Transaction>();
                if (tx == null)
                {
                    return false;
                }

                if (tx.Attributes.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var attributes = tx.Attributes
                    .Select(p => StackItem.FromInterface(p))
                    .ToArray();

                engine.CurrentContext.EvaluationStack.Push(attributes);
                return true;
            }

            return false;
        }

        private bool Transaction_GetInputs(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var tx = interopInterface.GetInterface<Transaction>();
                if (tx == null || tx.Inputs.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var inputs = tx.Inputs
                    .Select(p => StackItem.FromInterface(p))
                    .ToArray();

                engine.CurrentContext.EvaluationStack.Push(inputs);
                return true;
            }

            return false;
        }

        private bool Transaction_GetOutputs(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var tx = interopInterface.GetInterface<Transaction>();
                if (tx == null || tx.Outputs.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var outputs = tx.Outputs
                    .Select(p => StackItem.FromInterface(p))
                    .ToArray();

                engine.CurrentContext.EvaluationStack.Push(outputs);
                return true;
            }

            return false;
        }

        private bool Transaction_GetReferences(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var transaction = interopInterface.GetInterface<Transaction>();
                if (transaction == null || transaction.Inputs.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var references = transaction.Inputs
                    .Select(p => StackItem.FromInterface(transaction.References[p]))
                    .ToArray();

                engine.CurrentContext.EvaluationStack.Push(references);
                return true;
            }

            return false;
        }

        private bool Transaction_GetUnspentCoins(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var transaction = interopInterface.GetInterface<Transaction>();
                if (transaction == null)
                {
                    return false;
                }

                var outputs = this.Snapshot.GetUnspent(transaction.Hash).ToArray();
                if (outputs.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var unspentCoins = outputs.Select(p => StackItem.FromInterface(p)).ToArray();
                engine.CurrentContext.EvaluationStack.Push(unspentCoins);
                return true;
            }

            return false;
        }

        private bool Transaction_GetWitnesses(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var transaction = interopInterface.GetInterface<Transaction>();
                if (transaction == null || transaction.Witnesses.Length > ApplicationEngine.MaxArraySize)
                {
                    return false;
                }

                var witnesses = WitnessWrapper.Create(transaction, Snapshot)
                    .Select(p => StackItem.FromInterface(p))
                    .ToArray();

                engine.CurrentContext.EvaluationStack.Push(witnesses);
                return true;
            }

            return false;
        }

        private bool InvocationTransaction_GetScript(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var transaction = interopInterface.GetInterface<InvocationTransaction>();
                if (transaction == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(transaction.Script);
                return true;
            }

            return false;
        }

        private bool Witness_GetVerificationScript(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var witness = interopInterface.GetInterface<WitnessWrapper>();
                if (witness == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(witness.VerificationScript);
                return true;
            }

            return false;
        }

        private bool Attribute_GetUsage(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var attribute = interopInterface.GetInterface<TransactionAttribute>();
                if (attribute == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push((int)attribute.Usage);
                return true;
            }

            return false;
        }

        private bool Attribute_GetData(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var attribute = interopInterface.GetInterface<TransactionAttribute>();
                if (attribute == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(attribute.Data);
                return true;
            }

            return false;
        }

        private bool Input_GetHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var input = interopInterface.GetInterface<CoinReference>();
                if (input == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(input.PrevHash.ToArray());
                return true;
            }

            return false;
        }

        private bool Input_GetIndex(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var input = interopInterface.GetInterface<CoinReference>();
                if (input == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push((int)input.PrevIndex);
                return true;
            }

            return false;
        }

        private bool Output_GetAssetId(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var output = interopInterface.GetInterface<TransactionOutput>();
                if (output == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(output.AssetId.ToArray());
                return true;
            }

            return false;
        }

        private bool Output_GetValue(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var output = interopInterface.GetInterface<TransactionOutput>();
                if (output == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(output.Value.GetData());
                return true;
            }

            return false;
        }

        private bool Output_GetScriptHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var output = interopInterface.GetInterface<TransactionOutput>();
                if (output == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(output.ScriptHash.ToArray());
                return true;
            }

            return false;
        }

        private bool Account_GetScriptHash(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var account = interopInterface.GetInterface<AccountState>();
                if (account == null)
                {
                    return false;
                }

                var accountHash = account.ScriptHash.ToArray();
                engine.CurrentContext.EvaluationStack.Push(accountHash);
                return true;
            }

            return false;
        }

        private bool Account_GetVotes(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var account = interopInterface.GetInterface<AccountState>();
                if (account == null)
                {
                    return false;
                }

                var votes = account.Votes.Select(p => (StackItem)p.EncodePoint(true)).ToArray();
                engine.CurrentContext.EvaluationStack.Push(votes);
                return true;
            }

            return false;
        }

        private bool Account_GetBalance(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var account = interopInterface.GetInterface<AccountState>();
                var assetIdBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
                var assetId = new UInt256(assetIdBytes);
                if (account == null)
                {
                    return false;
                }

                var balance = account.Balances.TryGetValue(assetId, out Fixed8 value) 
                    ? value 
                    : Fixed8.Zero;

                engine.CurrentContext.EvaluationStack.Push(balance.GetData());
                return true;
            }

            return false;
        }

        private bool Account_IsStandard(ExecutionEngine engine)
        {
            var hashBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var hash = new UInt160(hashBytes);
            var contract = this.Snapshot.Contracts.TryGet(hash);
            var isStandard = contract is null || contract.Script.IsStandardContract();

            engine.CurrentContext.EvaluationStack.Push(isStandard);
            return true;
        }

        private bool Asset_Create(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application)
            {
                return false;
            }

            var transaction = (InvocationTransaction)engine.ScriptContainer;
            var assetType = (AssetType)(byte)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
            if (!Enum.IsDefined(typeof(AssetType), assetType) 
                || assetType == AssetType.CreditFlag 
                || assetType == AssetType.DutyFlag 
                || assetType == AssetType.GoverningToken 
                || assetType == AssetType.UtilityToken)
            {
                return false;
            }

            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 1024)
            {
                return false;
            }

            var nameBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var name = Encoding.UTF8.GetString(nameBytes);
            var amountLong = (long)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
            var amount = new Fixed8(amountLong);
            if (amount == Fixed8.Zero || amount < -Fixed8.Satoshi)
            {
                return false;
            }

            if (assetType == AssetType.Invoice && amount != -Fixed8.Satoshi)
            {
                return false;
            }

            var precision = (byte)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
            if (precision > 8)
            {
                return false;
            }

            if (assetType == AssetType.Share && precision != 0)
            {
                return false;
            }

            if (amount != -Fixed8.Satoshi && amount.GetData() % (long)Math.Pow(10, 8 - precision) != 0)
            {
                return false;
            }

            var ownerBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var owner = ECPoint.DecodePoint(ownerBytes, ECCurve.Secp256r1);
            if (owner.IsInfinity)
            {
                return false;
            }

            if (!this.CheckWitness(engine, owner))
            {
                return false;
            }

            var adminBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var admin = new UInt160(adminBytes);

            var issuerBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var issuer = new UInt160(issuerBytes);

            var assetState = Snapshot.Assets.GetOrAdd(
                transaction.Hash, 
                () => new AssetState
                {
                    AssetId = transaction.Hash,
                    Type = assetType,
                    Name = name,
                    Amount = amount,
                    Available = Fixed8.Zero,
                    Precision = precision,
                    Fee = Fixed8.Zero,
                    FeeAddress = new UInt160(),
                    Owner = owner,
                    Admin = admin,
                    Issuer = issuer,
                    Expiration = this.Snapshot.Height + 1 + 2000000,
                    IsFrozen = false
                });

            var asset = StackItem.FromInterface(assetState);
            engine.CurrentContext.EvaluationStack.Push(asset);
            return true;
        }

        private bool Asset_Renew(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application)
            {
                return false;
            }

            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var years = (byte)engine.CurrentContext.EvaluationStack.Pop().GetBigInteger();
                assetState = this.Snapshot.Assets.GetAndChange(assetState.AssetId);
                if (assetState.Expiration < this.Snapshot.Height + 1)
                {
                    assetState.Expiration = this.Snapshot.Height + 1;
                }

                try
                {
                    assetState.Expiration = checked(assetState.Expiration + (years * 2000000u));
                }
                catch (OverflowException)
                {
                    assetState.Expiration = uint.MaxValue;
                }

                engine.CurrentContext.EvaluationStack.Push(assetState.Expiration);
                return true;
            }

            return false;
        }

        private bool Asset_GetAssetId(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var assetId = assetState.AssetId.ToArray();
                engine.CurrentContext.EvaluationStack.Push(assetId);
                return true;
            }

            return false;
        }

        private bool Asset_GetAssetType(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var assetType = (int)assetState.Type;
                engine.CurrentContext.EvaluationStack.Push(assetType);
                return true;
            }

            return false;
        }

        private bool Asset_GetAmount(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var amount = assetState.Amount.GetData();
                engine.CurrentContext.EvaluationStack.Push(amount);
                return true;
            }

            return false;
        }

        private bool Asset_GetAvailable(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var available = assetState.Available.GetData();
                engine.CurrentContext.EvaluationStack.Push(available);
                return true;
            }

            return false;
        }

        private bool Asset_GetPrecision(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push((int)assetState.Precision);
                return true;
            }

            return false;
        }

        private bool Asset_GetOwner(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var assetState = interopInterface.GetInterface<AssetState>();
                if (assetState == null)
                {
                    return false;
                }

                var owner = assetState.Owner.EncodePoint(true);
                engine.CurrentContext.EvaluationStack.Push(owner);
                return true;
            }

            return false;
        }

        private bool Asset_GetAdmin(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var asset = interopInterface.GetInterface<AssetState>();
                if (asset == null)
                {
                    return false;
                }

                var admin = asset.Admin.ToArray();
                engine.CurrentContext.EvaluationStack.Push(admin);
                return true;
            }

            return false;
        }

        private bool Asset_GetIssuer(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var asset = interopInterface.GetInterface<AssetState>();
                if (asset == null)
                {
                    return false;
                }

                var issuer = asset.Issuer.ToArray();
                engine.CurrentContext.EvaluationStack.Push(issuer);
                return true;
            }

            return false;
        }

        private bool Contract_Create(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application)
            {
                return false;
            }

            var script = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            if (script.Length > 1024 * 1024)
            {
                return false;
            }

            var parameterList = engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetByteArray()
                .Select(p => (ContractParameterType)p)
                .ToArray();

            if (parameterList.Length > 252)
            {
                return false;
            }

            var returnType = (ContractParameterType)(byte)engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetBigInteger();

            var contractProperties = (ContractPropertyState)(byte)engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetBigInteger();

            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var contractNameBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var contractName = Encoding.UTF8.GetString(contractNameBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var versionBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var version = Encoding.UTF8.GetString(versionBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var authorNameBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var authorName = Encoding.UTF8.GetString(authorNameBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var emailBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var email = Encoding.UTF8.GetString(emailBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 65536)
            {
                return false;
            }

            var descriptionBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var description = Encoding.UTF8.GetString(descriptionBytes);
            var hash = script.ToScriptHash();
            var contract = Snapshot.Contracts.TryGet(hash);
            if (contract == null)
            {
                contract = new ContractState
                {
                    Script = script,
                    ParameterList = parameterList,
                    ReturnType = returnType,
                    ContractProperties = contractProperties,
                    Name = contractName,
                    CodeVersion = version,
                    Author = authorName,
                    Email = email,
                    Description = description
                };

                this.Snapshot.Contracts.Add(hash, contract);

                var contractHash = new UInt160(engine.CurrentContext.ScriptHash);
                this.ContractsCreated.Add(hash, contractHash);
            }

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(contract));
            return true;
        }

        private bool Contract_Migrate(ExecutionEngine engine)
        {
            if (this.Trigger != TriggerType.Application)
            {
                return false;
            }

            var script = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            if (script.Length > 1024 * 1024)
            {
                return false;
            }

            var parameters = engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetByteArray()
                .Select(p => (ContractParameterType)p)
                .ToArray();

            if (parameters.Length > 252)
            {
                return false;
            }

            var returnType = (ContractParameterType)(byte)engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetBigInteger();

            var contractProperties = (ContractPropertyState)(byte)engine.CurrentContext
                .EvaluationStack
                .Pop()
                .GetBigInteger();

            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var contractNameBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var contractName = Encoding.UTF8.GetString(contractNameBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var versionBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var version = Encoding.UTF8.GetString(versionBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var authorNameBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var authorName = Encoding.UTF8.GetString(authorNameBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 252)
            {
                return false;
            }

            var emailBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var email = Encoding.UTF8.GetString(emailBytes);
            if (engine.CurrentContext.EvaluationStack.Peek().GetByteArray().Length > 65536)
            {
                return false;
            }

            var descriptionBytes = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
            var description = Encoding.UTF8.GetString(descriptionBytes);
            var hash = script.ToScriptHash();
            var contractState = this.Snapshot.Contracts.TryGet(hash);
            if (contractState == null)
            {
                contractState = new ContractState
                {
                    Script = script,
                    ParameterList = parameters,
                    ReturnType = returnType,
                    ContractProperties = contractProperties,
                    Name = contractName,
                    CodeVersion = version,
                    Author = authorName,
                    Email = email,
                    Description = description
                };

                this.Snapshot.Contracts.Add(hash, contractState);

                var contractHash = new UInt160(engine.CurrentContext.ScriptHash);
                this.ContractsCreated.Add(hash, contractHash);
                if (contractState.HasStorage)
                {
                    var storages = this.Snapshot.Storages.Find(engine.CurrentContext.ScriptHash).ToArray();
                    foreach (var storage in storages)
                    {
                        var key = new StorageKey
                        {
                            ScriptHash = hash,
                            Key = storage.Key.Key
                        };

                        var item = new StorageItem
                        {
                            Value = storage.Value.Value,
                            IsConstant = false
                        };

                        this.Snapshot.Storages.Add(key, item);
                    }
                }
            }

            var contract = StackItem.FromInterface(contractState);
            engine.CurrentContext.EvaluationStack.Push(contract);
            return this.Contract_Destroy(engine);
        }

        private bool Contract_GetScript(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var contract = interopInterface.GetInterface<ContractState>();
                if (contract == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(contract.Script);
                return true;
            }

            return false;
        }

        private bool Contract_IsPayable(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var contract = interopInterface.GetInterface<ContractState>();
                if (contract == null)
                {
                    return false;
                }

                engine.CurrentContext.EvaluationStack.Push(contract.Payable);
                return true;
            }

            return false;
        }

        private bool Storage_Find(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var context = interopInterface.GetInterface<StorageContext>();
                if (!this.CheckStorageContext(context))
                {
                    return false;
                }

                var prefix = engine.CurrentContext.EvaluationStack.Pop().GetByteArray();
                byte[] prefixKey;
                using (var ms = new MemoryStream())
                {
                    int index = 0;
                    int remain = prefix.Length;
                    while (remain >= 16)
                    {
                        ms.Write(prefix, index, 16);
                        ms.WriteByte(0);
                        index += 16;
                        remain -= 16;
                    }

                    if (remain > 0)
                    {
                        ms.Write(prefix, index, remain);
                    }

                    prefixKey = context
                        .ScriptHash
                        .ToArray()
                        .Concat(ms.ToArray())
                        .ToArray();
                }

                var enumerator = this.Snapshot
                    .Storages
                    .Find(prefixKey)
                    .Where(p => p.Key.Key.Take(prefix.Length).SequenceEqual(prefix))
                    .GetEnumerator();

                var iterator = new StorageIterator(enumerator);

                engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(iterator));
                this.Disposables.Add(iterator);
                return true;
            }

            return false;
        }

        private bool Enumerator_Create(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is VMArray array)
            {
                var enumeratorWrapper = new ArrayWrapper(array);
                var enumerator = StackItem.FromInterface(enumeratorWrapper);
                engine.CurrentContext.EvaluationStack.Push(enumerator);
                return true;
            }

            return false;
        }

        private bool Enumerator_Next(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var enumerator = interopInterface.GetInterface<IEnumerator>();
                engine.CurrentContext.EvaluationStack.Push(enumerator.Next());
                return true;
            }

            return false;
        }

        private bool Enumerator_Value(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var enumerator = interopInterface.GetInterface<IEnumerator>();
                engine.CurrentContext.EvaluationStack.Push(enumerator.Value());
                return true;
            }

            return false;
        }

        private bool Enumerator_Concat(ExecutionEngine engine)
        {
            if (!(engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface1))
            {
                return false;
            }

            if (!(engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface2))
            {
                return false;
            }

            var first = interopInterface1.GetInterface<IEnumerator>();
            var second = interopInterface2.GetInterface<IEnumerator>();
            var concatenated = new ConcatenatedEnumerator(first, second);

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(concatenated));

            return true;
        }

        private bool Iterator_Create(ExecutionEngine engine)
        {
            IIterator iterator;
            switch (engine.CurrentContext.EvaluationStack.Pop())
            {
                case VMArray array:
                    iterator = new ArrayWrapper(array);
                    break;
                case Map map:
                    iterator = new MapWrapper(map);
                    break;
                default:
                    return false;
            }

            engine.CurrentContext.EvaluationStack.Push(StackItem.FromInterface(iterator));
            return true;
        }

        private bool Iterator_Key(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var iterator = interopInterface.GetInterface<IIterator>();
                engine.CurrentContext.EvaluationStack.Push(iterator.Key());
                return true;
            }

            return false;
        }

        private bool Iterator_Keys(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var iterator = interopInterface.GetInterface<IIterator>();

                var iteratorKeysWrapper = new IteratorKeysWrapper(iterator);
                var iteratorKeys = StackItem.FromInterface(iteratorKeysWrapper);
                engine.CurrentContext.EvaluationStack.Push(iteratorKeys);
                return true;
            }

            return false;
        }

        private bool Iterator_Values(ExecutionEngine engine)
        {
            if (engine.CurrentContext.EvaluationStack.Pop() is InteropInterface interopInterface)
            {
                var iterator = interopInterface.GetInterface<IIterator>();

                var iteratorValuesWrapper = new IteratorValuesWrapper(iterator);
                var iteratorValues = StackItem.FromInterface(iteratorValuesWrapper);
                engine.CurrentContext.EvaluationStack.Push(iteratorValues);
                return true;
            }

            return false;
        }
    }
}
