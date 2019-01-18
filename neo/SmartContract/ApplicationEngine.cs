using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Neo.Extensions;
using Neo.Ledger;
using Neo.Ledger.States;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;

namespace Neo.SmartContract
{
    public class ApplicationEngine : ExecutionEngine
    {
        /// <summary>
        /// Max value for SHL and SHR
        /// </summary>
        public const int MaxShlShr = ushort.MaxValue;

        /// <summary>
        /// Min value for SHL and SHR
        /// </summary>
        public const int MinShlShr = -MaxShlShr;

        /// <summary>
        /// Set the max size allowed size for BigInteger
        /// </summary>
        public const int MaxSizeForBigInteger = 32;

        /// <summary>
        /// Set the max Stack Size
        /// </summary>
        public const uint MaxStackSize = 2 * 1024;

        /// <summary>
        /// Set Max Item Size
        /// </summary>
        public const uint MaxItemSize = 1024 * 1024;

        /// <summary>
        /// Set Max Invocation Stack Size
        /// </summary>
        public const uint MaxInvocationStackSize = 1024;

        /// <summary>
        /// Set Max Array Size
        /// </summary>
        public const uint MaxArraySize = 1024;

        private const long Ratio = 100000;
        private const long GasFree = 10 * 100000000;

        private readonly long gasAmount;
        private readonly bool testMode;
        private readonly Snapshot snapshot;

        private long gasConsumed = 0;
        private int stackitemCount = 0;
        private bool isStackitemCountStrict = true;

        public ApplicationEngine(
            TriggerType trigger, 
            IScriptContainer container, 
            Snapshot snapshot, 
            Fixed8 gas, 
            bool testMode = false)
            : base(container, Cryptography.Crypto.Default, snapshot, new NeoService(trigger, snapshot))
        {
            this.gasAmount = ApplicationEngine.GasFree + gas.GetData();
            this.testMode = testMode;
            this.snapshot = snapshot;
        }

        public Fixed8 GasConsumed => new Fixed8(this.gasConsumed);

        public new NeoService Service => (NeoService)base.Service;

        public static ApplicationEngine Run(
            byte[] script,
            Snapshot snapshot,
            IScriptContainer container = null,
            Block persistingBlock = null,
            bool testMode = false,
            Fixed8 extraGAS = default(Fixed8))
        {
            snapshot.PersistingBlock = persistingBlock ?? snapshot.PersistingBlock ?? new Block
            {
                Version = 0,
                PrevHash = snapshot.CurrentBlockHash,
                MerkleRoot = new UInt256(),
                Timestamp = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.Timestamp + Blockchain.SecondsPerBlock,
                Index = snapshot.Height + 1,
                ConsensusData = 0,
                NextConsensus = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.NextConsensus,
                Witness = new Witness
                {
                    InvocationScript = new byte[0],
                    VerificationScript = new byte[0]
                },
                Transactions = new Transaction[0]
            };

            var engine = new ApplicationEngine(TriggerType.Application, container, snapshot, extraGAS, testMode);
            engine.LoadScript(script);
            engine.Execute();

            return engine;
        }

        public static ApplicationEngine Run(
            byte[] script, 
            IScriptContainer container = null, 
            Block persistingBlock = null, 
            bool testMode = false, 
            Fixed8 extraGAS = default(Fixed8))
        {
            using (var snapshot = Blockchain.Instance.GetSnapshot())
            {
                return ApplicationEngine.Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            this.Service.Dispose();
        }

        public new bool Execute()
        {
            try
            {
                while (true)
                {
                    var nextOpcode = this.CurrentContext.InstructionPointer >= this.CurrentContext.Script.Length 
                        ? OpCode.RET 
                        : this.CurrentContext.NextInstruction;

                    if (!this.PreStepInto(nextOpcode))
                    {
                        this.State |= VMState.FAULT;
                        return false;
                    }

                    this.StepInto();
                    if (this.State.HasAnyFlags(VMState.HALT, VMState.FAULT))
                    {
                        break;
                    }

                    if (!this.PostStepInto(nextOpcode))
                    {
                        this.State |= VMState.FAULT;
                        return false;
                    }
                }
            }
            catch
            {
                this.State |= VMState.FAULT;
                return false;
            }

            return !this.State.HasFlag(VMState.FAULT);
        }

        protected virtual long GetPrice(OpCode nextInstruction)
        {
            if (nextInstruction <= OpCode.NOP)
            {
                return 0;
            }

            switch (nextInstruction)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    return 10;
                case OpCode.SYSCALL:
                    return this.GetPriceForSysCall();
                case OpCode.SHA1:
                case OpCode.SHA256:
                    return 10;
                case OpCode.HASH160:
                case OpCode.HASH256:
                    return 20;
                case OpCode.CHECKSIG:
                case OpCode.VERIFY:
                    return 100;
                case OpCode.CHECKMULTISIG:
                    {
                        if (this.CurrentContext.EvaluationStack.Count == 0)
                        {
                            return 1;
                        }

                        var item = this.CurrentContext.EvaluationStack.Peek();

                        int n;
                        if (item is Array array)
                        {
                            n = array.Count;
                        }
                        else
                        {
                            n = (int)item.GetBigInteger();
                        }

                        if (n < 1)
                        {
                            return 1;
                        }

                        return 100 * n;
                    }

                default: return 1;
            }
        }

        protected virtual long GetPriceForSysCall()
        {
            if (this.CurrentContext.InstructionPointer >= this.CurrentContext.Script.Length - 3)
            {
                return 1;
            }

            var length = this.CurrentContext.Script[this.CurrentContext.InstructionPointer + 1];
            if (this.CurrentContext.InstructionPointer > this.CurrentContext.Script.Length - length - 2)
            {
                return 1;
            }

            var apiHash = length == 4
                ? System.BitConverter.ToUInt32(this.CurrentContext.Script, this.CurrentContext.InstructionPointer + 2)
                : Encoding.ASCII
                    .GetString(this.CurrentContext.Script, this.CurrentContext.InstructionPointer + 2, length)
                    .ToInteropMethodHash();

            var price = this.Service.GetPrice(apiHash);
            if (price > 0)
            {
                return price;
            }

            if (apiHash == "Neo.Asset.Create".ToInteropMethodHash()
                || apiHash == "AntShares.Asset.Create".ToInteropMethodHash())
            {
                return 5000L * 100000000L / Ratio;
            }

            if (apiHash == "Neo.Asset.Renew".ToInteropMethodHash() 
                || apiHash == "AntShares.Asset.Renew".ToInteropMethodHash())
            {
                return (byte)this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger() * 5000L * 100000000L / Ratio;
            }

            if (apiHash == "Neo.Contract.Create".ToInteropMethodHash() 
                || apiHash == "Neo.Contract.Migrate".ToInteropMethodHash() 
                || apiHash == "AntShares.Contract.Create".ToInteropMethodHash() 
                || apiHash == "AntShares.Contract.Migrate".ToInteropMethodHash())
            {
                var fee = 100L;
                var contractProperties = (ContractPropertyStates)(byte)this.CurrentContext
                    .EvaluationStack
                    .Peek(3)
                    .GetBigInteger();

                if (contractProperties.HasFlag(ContractPropertyStates.HasStorage))
                {
                    fee += 400L;
                }

                if (contractProperties.HasFlag(ContractPropertyStates.HasDynamicInvoke))
                {
                    fee += 500L;
                }

                return fee * 100000000L / Ratio;
            }

            if (apiHash == "System.Storage.Put".ToInteropMethodHash() 
                || apiHash == "System.Storage.PutEx".ToInteropMethodHash() 
                || apiHash == "Neo.Storage.Put".ToInteropMethodHash() 
                || apiHash == "AntShares.Storage.Put".ToInteropMethodHash())
            {
                var result = (((this.CurrentContext.EvaluationStack.Peek(1).GetByteArray().Length + this.CurrentContext.EvaluationStack.Peek(2).GetByteArray().Length - 1) / 1024) + 1) * 1000;
                return result;
            }

            return 1;
        }

        private static int GetItemCount(IEnumerable<StackItem> items)
        {
            var queue = new Queue<StackItem>(items);
            var counted = new List<StackItem>();
            var count = 0;
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                count++;
                switch (item)
                {
                    case Array array:
                        if (counted.Any(p => object.ReferenceEquals(p, array)))
                        {
                            continue;
                        }

                        counted.Add(array);
                        foreach (StackItem subitem in array)
                        {
                            queue.Enqueue(subitem);
                        }

                        break;
                    case Map map:
                        if (counted.Any(p => object.ReferenceEquals(p, map)))
                        {
                            continue;
                        }

                        counted.Add(map);
                        foreach (StackItem subitem in map.Values)
                        {
                            queue.Enqueue(subitem);
                        }

                        break;
                }
            }

            return count;
        }

        private bool CheckArraySize(OpCode nextInstruction)
        {
            int size;
            switch (nextInstruction)
            {
                case OpCode.PACK:
                case OpCode.NEWARRAY:
                case OpCode.NEWSTRUCT:
                    {
                        if (this.CurrentContext.EvaluationStack.Count == 0)
                        {
                            return false;
                        }

                        size = (int)this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                    }

                    break;
                case OpCode.SETITEM:
                    {
                        if (this.CurrentContext.EvaluationStack.Count < 3)
                        {
                            return false;
                        }

                        if (!(this.CurrentContext.EvaluationStack.Peek(2) is Map map))
                        {
                            return true;
                        }

                        var key = this.CurrentContext.EvaluationStack.Peek(1);
                        if (key is ICollection)
                        {
                            return false;
                        }

                        if (map.ContainsKey(key))
                        {
                            return true;
                        }

                        size = map.Count + 1;
                    }

                    break;
                case OpCode.APPEND:
                    {
                        if (this.CurrentContext.EvaluationStack.Count < 2)
                        {
                            return false;
                        }

                        if (!(this.CurrentContext.EvaluationStack.Peek(1) is Array array))
                        {
                            return false;
                        }

                        size = array.Count + 1;
                    }

                    break;
                default:
                    return true;
            }

            return size <= ApplicationEngine.MaxArraySize;
        }

        private bool CheckInvocationStack(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.CALL:
                case OpCode.APPCALL:
                case OpCode.CALL_I:
                case OpCode.CALL_E:
                case OpCode.CALL_ED:
                    if (this.InvocationStack.Count >= ApplicationEngine.MaxInvocationStackSize)
                    {
                        return false;
                    }

                    return true;
                default:
                    return true;
            }
        }

        private bool CheckItemSize(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.PUSHDATA4:
                    {
                        if (this.CurrentContext.InstructionPointer + 4 >= this.CurrentContext.Script.Length)
                        {
                            return false;
                        }

                        var length = this.CurrentContext.Script.ToUInt32(this.CurrentContext.InstructionPointer + 1);
                        if (length > MaxItemSize)
                        {
                            return false;
                        }

                        return true;
                    }

                case OpCode.CAT:
                    {
                        if (this.CurrentContext.EvaluationStack.Count < 2)
                        {
                            return false;
                        }

                        int length = this.CurrentContext.EvaluationStack.Peek(0).GetByteArray().Length + this.CurrentContext.EvaluationStack.Peek(1).GetByteArray().Length;
                        if (length > ApplicationEngine.MaxItemSize)
                        {
                            return false;
                        }

                        return true;
                    }

                default:
                    return true;
            }
        }

        /// <summary>
        /// Check if the BigInteger is allowed for numeric operations
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Return True if are allowed, otherwise False</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckBigInteger(BigInteger value) =>
            value.ToByteArray().Length <= ApplicationEngine.MaxSizeForBigInteger;

        /// <summary>
        /// Check if the BigInteger is allowed for numeric operations
        /// </summary>
        private bool CheckBigIntegers(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.SHL:
                    {
                        var ishift = this.CurrentContext.EvaluationStack.Peek(0).GetBigInteger();
                        if (ishift > ApplicationEngine.MaxShlShr || ishift < ApplicationEngine.MinShlShr)
                        {
                            return false;
                        }

                        var x = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x << (int)ishift))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.SHR:
                    {
                        var ishift = this.CurrentContext.EvaluationStack.Peek(0).GetBigInteger();
                        if (ishift > MaxShlShr || ishift < MinShlShr)
                        {
                            return false;
                        }

                        var x = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x >> (int)ishift))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.INC:
                    {
                        var x = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        if (!this.CheckBigInteger(x) || !this.CheckBigInteger(x + 1))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.DEC:
                    {
                        var x = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        if (!this.CheckBigInteger(x) || (x.Sign <= 0 && !this.CheckBigInteger(x - 1)))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.ADD:
                    {
                        var x2 = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        var x1 = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x2) || !this.CheckBigInteger(x1) || !this.CheckBigInteger(x1 + x2))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.SUB:
                    {
                        var x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        var x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x2) || !this.CheckBigInteger(x1) || !this.CheckBigInteger(x1 - x2))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.MUL:
                    {
                        var x2 = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        var x1 = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        var lx1 = x1.ToByteArray().Length;
                        if (lx1 > MaxSizeForBigInteger)
                        {
                            return false;
                        }

                        var lx2 = x2.ToByteArray().Length;
                        if ((lx1 + lx2) > ApplicationEngine.MaxSizeForBigInteger)
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.DIV:
                    {
                        var x2 = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        var x1 = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x2) || !this.CheckBigInteger(x1))
                        {
                            return false;
                        }

                        break;
                    }

                case OpCode.MOD:
                    {
                        var x2 = this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        var x1 = this.CurrentContext.EvaluationStack.Peek(1).GetBigInteger();
                        if (!this.CheckBigInteger(x2) || !this.CheckBigInteger(x1))
                        {
                            return false;
                        }

                        break;
                    }
            }

            return true;
        }

        private bool CheckStackSize(OpCode nextInstruction)
        {
            if (nextInstruction <= OpCode.PUSH16)
            {
                this.stackitemCount++;
            }
            else
            {
                switch (nextInstruction)
                {
                    case OpCode.JMPIF:
                    case OpCode.JMPIFNOT:
                    case OpCode.DROP:
                    case OpCode.NIP:
                    case OpCode.EQUAL:
                    case OpCode.BOOLAND:
                    case OpCode.BOOLOR:
                    case OpCode.CHECKMULTISIG:
                    case OpCode.REVERSE:
                    case OpCode.HASKEY:
                    case OpCode.THROWIFNOT:
                        this.stackitemCount -= 1;
                        this.isStackitemCountStrict = false;
                        break;
                    case OpCode.XSWAP:
                    case OpCode.ROLL:
                    case OpCode.CAT:
                    case OpCode.LEFT:
                    case OpCode.RIGHT:
                    case OpCode.AND:
                    case OpCode.OR:
                    case OpCode.XOR:
                    case OpCode.ADD:
                    case OpCode.SUB:
                    case OpCode.MUL:
                    case OpCode.DIV:
                    case OpCode.MOD:
                    case OpCode.SHL:
                    case OpCode.SHR:
                    case OpCode.NUMEQUAL:
                    case OpCode.NUMNOTEQUAL:
                    case OpCode.LT:
                    case OpCode.GT:
                    case OpCode.LTE:
                    case OpCode.GTE:
                    case OpCode.MIN:
                    case OpCode.MAX:
                    case OpCode.CHECKSIG:
                    case OpCode.CALL_ED:
                    case OpCode.CALL_EDT:
                        this.stackitemCount -= 1;
                        break;
                    case OpCode.RET:
                    case OpCode.APPCALL:
                    case OpCode.TAILCALL:
                    case OpCode.NOT:
                    case OpCode.ARRAYSIZE:
                        this.isStackitemCountStrict = false;
                        break;
                    case OpCode.SYSCALL:
                    case OpCode.PICKITEM:
                    case OpCode.SETITEM:
                    case OpCode.APPEND:
                    case OpCode.VALUES:
                        this.stackitemCount = int.MaxValue;
                        this.isStackitemCountStrict = false;
                        break;
                    case OpCode.DUPFROMALTSTACK:
                    case OpCode.DEPTH:
                    case OpCode.DUP:
                    case OpCode.OVER:
                    case OpCode.TUCK:
                    case OpCode.NEWMAP:
                        this.stackitemCount += 1;
                        break;
                    case OpCode.XDROP:
                    case OpCode.REMOVE:
                        this.stackitemCount -= 2;
                        this.isStackitemCountStrict = false;
                        break;
                    case OpCode.SUBSTR:
                    case OpCode.WITHIN:
                    case OpCode.VERIFY:
                        this.stackitemCount -= 2;
                        break;
                    case OpCode.UNPACK:
                        this.stackitemCount += (int)this.CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        this.isStackitemCountStrict = false;
                        break;
                    case OpCode.NEWARRAY:
                    case OpCode.NEWSTRUCT:
                        this.stackitemCount += ((Array)this.CurrentContext.EvaluationStack.Peek()).Count;
                        break;
                    case OpCode.KEYS:
                        this.stackitemCount += ((Array)this.CurrentContext.EvaluationStack.Peek()).Count;
                        this.isStackitemCountStrict = false;
                        break;
                }
            }

            if (this.stackitemCount <= MaxStackSize)
            {
                return true;
            }

            if (this.isStackitemCountStrict)
            {
                return false;
            }

            var stackItems = this.InvocationStack.SelectMany(p => p.EvaluationStack.Concat(p.AltStack));
            this.stackitemCount = ApplicationEngine.GetItemCount(stackItems);

            if (this.stackitemCount > ApplicationEngine.MaxStackSize)
            {
                return false;
            }

            this.isStackitemCountStrict = true;
            return true;
        }

        private bool CheckDynamicInvoke(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    for (int i = this.CurrentContext.InstructionPointer + 1; i < this.CurrentContext.InstructionPointer + 21; i++)
                    {
                        if (this.CurrentContext.Script[i] != 0)
                        {
                            return true;
                        }
                    }

                    // if we get this far it is a dynamic call
                    // now look at the current executing script
                    // to determine if it can do dynamic calls
                    return this.snapshot.Contracts[new UInt160(this.CurrentContext.ScriptHash)].HasDynamicInvoke;
                case OpCode.CALL_ED:
                case OpCode.CALL_EDT:
                    return this.snapshot.Contracts[new UInt160(this.CurrentContext.ScriptHash)].HasDynamicInvoke;
                default:
                    return true;
            }
        }

        private bool PostStepInto(OpCode nextOpcode)
        {
            if (!this.CheckStackSize(nextOpcode))
            {
                return false;
            }

            return true;
        }

        private bool PreStepInto(OpCode nextOpcode)
        {
            if (this.CurrentContext.InstructionPointer >= this.CurrentContext.Script.Length)
            {
                return true;
            }

            this.gasConsumed = checked(this.gasConsumed + (this.GetPrice(nextOpcode) * ApplicationEngine.Ratio));

            if (!this.testMode && this.gasConsumed > this.gasAmount)
            {
                return false;
            }

            if (!this.CheckItemSize(nextOpcode))
            {
                return false;
            }

            if (!this.CheckArraySize(nextOpcode))
            {
                return false;
            }

            if (!this.CheckInvocationStack(nextOpcode))
            {
                return false;
            }

            if (!this.CheckBigIntegers(nextOpcode))
            {
                return false;
            }

            if (!this.CheckDynamicInvoke(nextOpcode))
            {
                return false;
            }

            return true;
        }
    }
}
