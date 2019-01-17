using System;
using Neo.VM;

namespace Neo.SmartContract
{
    public class NotifyEventArgs : EventArgs
    {
        public NotifyEventArgs(IScriptContainer container, UInt160 script_hash, StackItem state)
        {
            this.ScriptContainer = container;
            this.ScriptHash = script_hash;
            this.State = state;
        }

        public IScriptContainer ScriptContainer { get; }

        public UInt160 ScriptHash { get; }

        public StackItem State { get; }
    }
}
