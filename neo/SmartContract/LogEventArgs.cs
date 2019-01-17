using System;
using Neo.VM;

namespace Neo.SmartContract
{
    public class LogEventArgs : EventArgs
    {
        public LogEventArgs(IScriptContainer container, UInt160 scriptHash, string message)
        {
            this.ScriptContainer = container;
            this.ScriptHash = scriptHash;
            this.Message = message;
        }

        public IScriptContainer ScriptContainer { get; }

        public UInt160 ScriptHash { get; }

        public string Message { get; }
    }
}
