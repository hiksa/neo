using System;
using System.Collections.Generic;
using Akka.Actor;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    internal class TaskSession
    {
        public readonly Dictionary<UInt256, DateTime> Tasks = new Dictionary<UInt256, DateTime>();
        public readonly HashSet<UInt256> AvailableTasks = new HashSet<UInt256>();

        public TaskSession(IActorRef node, VersionPayload version)
        {
            this.RemoteNode = node;
            this.Version = version;
        }

        public bool HasTask => this.Tasks.Count > 0;

        public bool HeaderTask => this.Tasks.ContainsKey(UInt256.Zero);

        public IActorRef RemoteNode { get; private set; }

        public VersionPayload Version { get; private set; }
    }
}
