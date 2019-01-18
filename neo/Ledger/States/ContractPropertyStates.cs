using System;

namespace Neo.Ledger.States
{
    [Flags]
    public enum ContractPropertyStates : byte
    {
        NoProperty = 0,

        HasStorage = 1 << 0,
        HasDynamicInvoke = 1 << 1,
        Payable = 1 << 2
    }
}
