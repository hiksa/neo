using System;
using System.Net;
using Akka.Actor;
using Neo.Consensus;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.RPC;
using Neo.Persistence;
using Neo.Plugins;
using Neo.Wallets;

namespace Neo
{
    public class NeoSystem : IDisposable
    {
        private Peer.Start startMessage = null;
        private bool suspend = false;

        public NeoSystem(Store store)
        {
            this.BlockchainActorRef = ActorSystem.ActorOf(Blockchain.Props(this, store));
            this.LocalNodeActorRef = ActorSystem.ActorOf(LocalNode.Props(this));
            this.TaskManagerActorRef = ActorSystem.ActorOf(TaskManager.Props(this));

            Plugin.LoadPlugins(this);
        }

        public ActorSystem ActorSystem { get; } = ActorSystem.Create(
            nameof(NeoSystem),
            $"akka {{ log-dead-letters = off }}" +
            $"blockchain-mailbox {{ mailbox-type: \"{typeof(BlockchainMailbox).AssemblyQualifiedName}\" }}" +
            $"task-manager-mailbox {{ mailbox-type: \"{typeof(TaskManagerMailbox).AssemblyQualifiedName}\" }}" +
            $"remote-node-mailbox {{ mailbox-type: \"{typeof(RemoteNodeMailbox).AssemblyQualifiedName}\" }}" +
            $"protocol-handler-mailbox {{ mailbox-type: \"{typeof(ProtocolHandlerMailbox).AssemblyQualifiedName}\" }}" +
            $"consensus-service-mailbox {{ mailbox-type: \"{typeof(ConsensusServiceMailbox).AssemblyQualifiedName}\" }}");

        public IActorRef BlockchainActorRef { get; }

        public IActorRef LocalNodeActorRef { get; }

        public IActorRef TaskManagerActorRef { get; }

        public IActorRef ConsensusServiceActorRef { get; private set; }

        public RpcServer RpcServer { get; private set; }

        public void StartConsensus(Wallet wallet)
        {
            var consensusServiceProps = ConsensusService.Props(this.LocalNodeActorRef, this.TaskManagerActorRef, wallet);

            this.ConsensusServiceActorRef = this.ActorSystem.ActorOf(consensusServiceProps);

            this.ConsensusServiceActorRef.Tell(new ConsensusService.Start());
        }

        public void StartNode(
            int port = 0, 
            int webSocketPort = 0,
            int minDesiredConnections = Peer.DefaultMinDesiredConnections,
            int maxConnections = Peer.DefaultMaxConnections)
        {
            this.startMessage = new Peer.Start(port, webSocketPort, minDesiredConnections, maxConnections);

            if (!this.suspend)
            {
                this.LocalNodeActorRef.Tell(this.startMessage);
                this.startMessage = null;
            }
        }

        public void StartRpc(
            IPAddress bindAddress, 
            int port, 
            Wallet wallet = null, 
            string sslCert = null, 
            string password = null,
            string[] trustedAuthorities = null, 
            Fixed8 maxGasInvoke = default(Fixed8))
        {
            this.RpcServer = new RpcServer(this, wallet, maxGasInvoke);
            this.RpcServer.Start(bindAddress, port, sslCert, password, trustedAuthorities);
        }

        public void Dispose()
        {
            this.RpcServer?.Dispose();
            this.ActorSystem.Stop(this.LocalNodeActorRef);
            this.ActorSystem.Dispose();
        }

        internal void SuspendNodeStartup()
        {
            this.suspend = true;
        }

        internal void ResumeNodeStartup()
        {
            this.suspend = false;

            if (this.startMessage != null)
            {
                this.LocalNodeActorRef.Tell(this.startMessage);
                this.startMessage = null;
            }
        }
    }
}
