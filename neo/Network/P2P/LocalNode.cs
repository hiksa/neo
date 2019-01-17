using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Neo.Extensions;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    public class LocalNode : Peer
    {
        public const uint ProtocolVersion = 0;

        public static readonly uint Nonce;

        internal readonly ConcurrentDictionary<IActorRef, RemoteNode> RemoteNodes = new ConcurrentDictionary<IActorRef, RemoteNode>();

        private static readonly object LockObj = new object();
        private static LocalNode instance;

        private readonly NeoSystem system;
        
        static LocalNode()
        {
            var rand = new Random();
            LocalNode.Nonce = (uint)rand.Next();

            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var assemblyVersion = Assembly.GetExecutingAssembly().GetVersion();

            LocalNode.UserAgent = $"/{assemblyName}:{assemblyVersion}/";
        }

        public LocalNode(NeoSystem system)
        {
            lock (LocalNode.LockObj)
            {
                if (instance != null)
                {
                    throw new InvalidOperationException();
                }

                this.system = system;
                instance = this;
            }
        }

        public static LocalNode Instance
        {
            get
            {
                while (instance == null)
                {
                    Thread.Sleep(10);
                }

                return instance;
            }
        }

        public static string UserAgent { get; set; }

        public int ConnectedCount => this.RemoteNodes.Count;

        public int UnconnectedCount => this.UnconnectedPeers.Count;

        public static Props Props(NeoSystem system) =>
            Akka.Actor.Props.Create(() => new LocalNode(system));

        public IEnumerable<RemoteNode> GetRemoteNodes() => this.RemoteNodes.Values;

        public IEnumerable<IPEndPoint> GetUnconnectedPeers() => this.UnconnectedPeers;

        protected override void NeedMorePeers(int count)
        {
            if (this.ConnectedPeers.Count > 0)
            {
                this.BroadcastMessage("getaddr");
            }
            else
            {
                var maxCount = Math.Max(count, 5);
                var moreEndpoints = LocalNode.GetIPEndPointsFromSeedList(maxCount);
                this.AddPeers(moreEndpoints);
            }
        }

        protected override void OnReceive(object message)
        {
            base.OnReceive(message);
            switch (message)
            {
                case Message msg:
                    this.BroadcastMessage(msg);
                    break;
                case Relay relay:
                    this.OnRelay(relay.Inventory);
                    break;
                case RelayDirectly relay:
                    this.OnRelayDirectly(relay.Inventory);
                    break;
                case SendDirectly send:
                    this.OnSendDirectly(send.Inventory);
                    break;
                case RelayResultReason _:
                    break;
            }
        }
        
        protected override Props ProtocolProps(object connection, IPEndPoint remote, IPEndPoint local) =>
            RemoteNode.Props(this.system, connection, remote, local);
        
        private static IPEndPoint GetIPEndpointFromHostPort(string hostNameOrAddress, int port)
        {
            if (IPAddress.TryParse(hostNameOrAddress, out IPAddress ipAddress))
            {
                return new IPEndPoint(ipAddress, port);
            }

            IPHostEntry entry;
            try
            {
                entry = Dns.GetHostEntry(hostNameOrAddress);
            }
            catch (SocketException)
            {
                return null;
            }

            ipAddress = entry
                .AddressList
                .FirstOrDefault(p => p.AddressFamily == AddressFamily.InterNetwork || p.IsIPv6Teredo);

            if (ipAddress == null)
            {
                return null;
            }

            return new IPEndPoint(ipAddress, port);
        }

        private static IEnumerable<IPEndPoint> GetIPEndPointsFromSeedList(int seedsToTake)
        {
            if (seedsToTake > 0)
            {
                var seeds = ProtocolSettings.Default.SeedList.OrderBy(p => Guid.NewGuid());
                foreach (var address in seeds)
                {
                    var p = address.Split(':');
                    IPEndPoint seed;
                    try
                    {
                        seed = LocalNode.GetIPEndpointFromHostPort(p[0], int.Parse(p[1]));
                    }
                    catch (AggregateException)
                    {
                        continue;
                    }

                    if (seed == null)
                    {
                        continue;
                    }

                    seedsToTake--;

                    yield return seed;
                }
            }
        }

        private void BroadcastMessage(string command, ISerializable payload = null) =>
            this.BroadcastMessage(Message.Create(command, payload));

        private void BroadcastMessage(Message message) =>
            this.Connections.Tell(message);

        private void OnRelay(IInventory inventory)
        {
            if (inventory is Transaction transaction)
            {
                this.system.ConsensusServiceActorRef?.Tell(transaction);
            }

            this.system.BlockchainActorRef.Tell(inventory);
        }

        private void OnRelayDirectly(IInventory inventory) =>
            this.Connections.Tell(new RemoteNode.Relay(inventory));
        
        private void OnSendDirectly(IInventory inventory) =>
            this.Connections.Tell(inventory);

        public class Relay
        {
            public Relay(IInventory inventory)
            {
                this.Inventory = inventory;
            }

            public IInventory Inventory { get; private set; }
        }

        internal class RelayDirectly
        {
            public RelayDirectly(IInventory inventory)
            {
                this.Inventory = inventory;
            }

            public IInventory Inventory { get; private set; }
        }

        internal class SendDirectly
        {
            public SendDirectly(IInventory inventory)
            {
                this.Inventory = inventory;
            }

            public IInventory Inventory { get; private set; }
        }
    }
}
