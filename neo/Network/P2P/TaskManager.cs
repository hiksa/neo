using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    internal class TaskManager : UntypedActor
    {
        private const int MaxConncurrentTasks = 3;

        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(1);

        private readonly NeoSystem system;
        private readonly HashSet<UInt256> knownHashes = new HashSet<UInt256>();
        private readonly Dictionary<UInt256, int> globalTasks = new Dictionary<UInt256, int>();
        private readonly Dictionary<IActorRef, TaskSession> sessions = new Dictionary<IActorRef, TaskSession>();
        private readonly ICancelable timer = Context.System.Scheduler
            .ScheduleTellRepeatedlyCancelable(
                TimerInterval, 
                TimerInterval, 
                Context.Self, 
                new Timer(), 
                ActorRefs.NoSender);

        private readonly UInt256 headerTaskHash = UInt256.Zero;

        public TaskManager(NeoSystem system)
        {
            this.system = system;
        }
        
        private bool HasHeaderTask => this.globalTasks.ContainsKey(this.headerTaskHash);

        public static Props Props(NeoSystem system) =>
            Akka.Actor.Props
                .Create(() => new TaskManager(system))
                .WithMailbox("task-manager-mailbox");

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Register register:
                    this.OnRegister(register.Version);
                    break;
                case NewTasks tasks:
                    this.OnNewTasks(tasks.Payload);
                    break;
                case TaskCompleted completed:
                    this.OnTaskCompleted(completed.Hash);
                    break;
                case HeaderTaskCompleted _:
                    this.OnHeaderTaskCompleted();
                    break;
                case RestartTasks restart:
                    this.OnRestartTasks(restart.Payload);
                    break;
                case Timer _:
                    this.OnTimer();
                    break;
                case Terminated terminated:
                    this.OnTerminated(terminated.ActorRef);
                    break;
            }
        }

        protected override void PostStop()
        {
            this.timer.CancelIfNotNull();
            base.PostStop();
        }

        private void OnRegister(VersionPayload version)
        {
            UntypedActor.Context.Watch(this.Sender);

            var session = new TaskSession(this.Sender, version);
            this.sessions.Add(this.Sender, session);

            this.RequestTasks(session);
        }

        private void OnRestartTasks(InvPayload payload)
        {
            this.knownHashes.ExceptWith(payload.Hashes);
            foreach (var hash in payload.Hashes)
            {
                this.globalTasks.Remove(hash);
            }

            foreach (var group in InvPayload.CreateMany(payload.Type, payload.Hashes))
            {
                var getDataMessage = Message.Create("getdata", group);
                this.system.LocalNodeActorRef.Tell(getDataMessage);
            }
        }

        private void OnTaskCompleted(UInt256 hash)
        {
            this.knownHashes.Add(hash);
            this.globalTasks.Remove(hash);

            foreach (var ms in this.sessions.Values)
            {
                ms.AvailableTasks.Remove(hash);
            }

            if (this.sessions.TryGetValue(this.Sender, out TaskSession session))
            {
                session.Tasks.Remove(hash);
                this.RequestTasks(session);
            }
        }

        private void OnHeaderTaskCompleted()
        {
            if (!this.sessions.TryGetValue(this.Sender, out TaskSession session))
            {
                return;
            }

            session.Tasks.Remove(this.headerTaskHash);

            this.DecrementGlobalTask(this.headerTaskHash);
            this.RequestTasks(session);
        }

        private void OnNewTasks(InvPayload payload)
        {
            if (!this.sessions.TryGetValue(this.Sender, out TaskSession session))
            {
                return;
            }

            if (payload.Type == InventoryType.TX 
                && Blockchain.Instance.Height < Blockchain.Instance.HeaderHeight)
            {
                this.RequestTasks(session);
                return;
            }

            var hashes = new HashSet<UInt256>(payload.Hashes);
            hashes.ExceptWith(this.knownHashes);
            if (payload.Type == InventoryType.Block)
            {
                session.AvailableTasks.UnionWith(hashes.Where(p => this.globalTasks.ContainsKey(p)));
            }

            hashes.ExceptWith(this.globalTasks.Keys);
            if (hashes.Count == 0)
            {
                this.RequestTasks(session);
                return;
            }

            foreach (var hash in hashes)
            {
                this.IncrementGlobalTask(hash);
                session.Tasks[hash] = DateTime.UtcNow;
            }

            var payloads = InvPayload.CreateMany(payload.Type, hashes.ToArray());
            foreach (var invPayload in payloads)
            {
                var getDataMessage = Message.Create("getdata", invPayload);
                Sender.Tell(getDataMessage);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecrementGlobalTask(UInt256 hash)
        {
            if (this.globalTasks.ContainsKey(hash))
            {
                if (this.globalTasks[hash] == 1)
                {
                    this.globalTasks.Remove(hash);
                }
                else
                {
                    this.globalTasks[hash]--;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncrementGlobalTask(UInt256 hash)
        {
            if (!this.globalTasks.ContainsKey(hash))
            {
                this.globalTasks[hash] = 1;
                return true;
            }

            if (this.globalTasks[hash] >= MaxConncurrentTasks)
            {
                return false;
            }

            this.globalTasks[hash]++;

            return true;
        }

        private void OnTerminated(IActorRef actor)
        {
            if (!this.sessions.TryGetValue(actor, out TaskSession session))
            {
                return;
            }

            this.sessions.Remove(actor);
            foreach (var hash in session.Tasks.Keys)
            {
                this.DecrementGlobalTask(hash);
            }
        }

        private void OnTimer()
        {
            foreach (var session in this.sessions.Values)
            {
                foreach (var task in session.Tasks.ToArray())
                {
                    if (DateTime.UtcNow - task.Value > TaskManager.TaskTimeout 
                        && session.Tasks.Remove(task.Key))
                    {
                        this.DecrementGlobalTask(task.Key);                        
                    }
                }
            }

            foreach (var session in this.sessions.Values)
            {
                this.RequestTasks(session);
            }
        }
        
        private void RequestTasks(TaskSession session)
        {
            if (session.HasTask)
            {
                return;
            }

            if (session.AvailableTasks.Count > 0)
            {
                session.AvailableTasks.ExceptWith(this.knownHashes);
                session.AvailableTasks.RemoveWhere(p => Blockchain.Instance.ContainsBlock(p));
                var hashes = new HashSet<UInt256>(session.AvailableTasks);
                if (hashes.Count > 0)
                {
                    foreach (var hash in hashes.ToArray())
                    {
                        if (!this.IncrementGlobalTask(hash))
                        {
                            hashes.Remove(hash);
                        }
                    }

                    session.AvailableTasks.ExceptWith(hashes);
                    foreach (var hash in hashes)
                    {
                        session.Tasks[hash] = DateTime.UtcNow;
                    }

                    var payloads = InvPayload.CreateMany(InventoryType.Block, hashes.ToArray());
                    foreach (var payload in payloads)
                    {
                        var getDataMessage = Message.Create("getdata", payload);
                        session.RemoteNode.Tell(getDataMessage);
                    }

                    return;
                }
            }

            if ((!this.HasHeaderTask || this.globalTasks[this.headerTaskHash] < TaskManager.MaxConncurrentTasks) 
                && Blockchain.Instance.HeaderHeight < session.Version.StartHeight)
            {
                session.Tasks[this.headerTaskHash] = DateTime.UtcNow;
                this.IncrementGlobalTask(this.headerTaskHash);

                var getHeadersPayload = GetBlocksPayload.Create(Blockchain.Instance.CurrentHeaderHash);
                var getHeadersMessage = Message.Create("getheaders", getHeadersPayload);
                session.RemoteNode.Tell(getHeadersMessage);
            }
            else if (Blockchain.Instance.Height < session.Version.StartHeight)
            {
                var hash = Blockchain.Instance.CurrentBlockHash;
                for (uint i = Blockchain.Instance.Height + 1; i <= Blockchain.Instance.HeaderHeight; i++)
                {
                    hash = Blockchain.Instance.GetBlockHash(i);
                    if (!this.globalTasks.ContainsKey(hash))
                    {
                        hash = Blockchain.Instance.GetBlockHash(i - 1);
                        break;
                    }
                }

                var getBlocksPayload = GetBlocksPayload.Create(hash);
                var getBlocksMessage = Message.Create("getblocks", getBlocksPayload);
                session.RemoteNode.Tell(getBlocksMessage);
            }
        }

        public class Register
        {
            public Register(VersionPayload version)
            {
                this.Version = version;
            }

            public VersionPayload Version { get; private set; }
        }

        public class NewTasks
        {
            public NewTasks(InvPayload payload)
            {
                this.Payload = payload;
            }

            public InvPayload Payload { get; private set; }
        }

        public class TaskCompleted
        {
            public TaskCompleted(UInt256 hash)
            {
                this.Hash = hash;
            }

            public UInt256 Hash { get; private set; }
        }

        public class HeaderTaskCompleted
        {
        }

        public class RestartTasks
        {
            public RestartTasks(InvPayload payload)
            {
                this.Payload = payload;
            }

            public InvPayload Payload { get; private set; }
        }

        private class Timer
        {
        }
    }
}
