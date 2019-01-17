using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Neo.Extensions;

namespace Neo.Network.P2P
{
    public abstract class Peer : UntypedActor
    {
        public const int DefaultMinDesiredConnections = 10;
        public const int DefaultMaxConnections = DefaultMinDesiredConnections * 4;
        
        protected readonly ConcurrentDictionary<IActorRef, IPEndPoint> ConnectedPeers = new ConcurrentDictionary<IActorRef, IPEndPoint>();
        protected ImmutableHashSet<IPEndPoint> UnconnectedPeers = ImmutableHashSet<IPEndPoint>.Empty;

        private const int UnconnectedMax = 1000;
        private const int MaxConnectionsPerAddress = 3;

        private static readonly IActorRef TcpManagerActorRef = Context.System.Tcp();
        private static readonly HashSet<IPAddress> LocalAddresses = new HashSet<IPAddress>();

        private readonly Dictionary<IPAddress, int> connectedAddresses = new Dictionary<IPAddress, int>();

        private ImmutableHashSet<IPEndPoint> connectingPeers = ImmutableHashSet<IPEndPoint>.Empty;

        private IActorRef tcpListenerActorRef;
        private IWebHost webSocketHost;
        private ICancelable timer;
        
        static Peer()
        {
            IEnumerable<IPAddress> remoteAddresses = NetworkInterface
                .GetAllNetworkInterfaces()
                .SelectMany(p => p.GetIPProperties().UnicastAddresses)
                .Select(p => p.Address.Unmap());

            Peer.LocalAddresses.UnionWith(remoteAddresses);
        }

        public int ListenerPort { get; private set; }

        public int MinimumDesiredConnections { get; private set; } = DefaultMinDesiredConnections;

        public int MaxConnections { get; private set; } = DefaultMaxConnections;

        protected ActorSelection Connections => Context.ActorSelection("connection_*");

        protected HashSet<IPAddress> TrustedIpAddresses { get; } = new HashSet<IPAddress>();
        
        protected virtual int ConnectingMax
        {
            get
            {
                var allowedConnecting = this.MinimumDesiredConnections * 4;
                allowedConnecting = this.MaxConnections != -1 && allowedConnecting > this.MaxConnections
                    ? this.MaxConnections
                    : allowedConnecting;

                return allowedConnecting - this.ConnectedPeers.Count;
            }
        }

        protected void AddPeers(IEnumerable<IPEndPoint> peers)
        {
            if (this.UnconnectedPeers.Count < UnconnectedMax)
            {
                peers = peers.Where(p => p.Port != this.ListenerPort || !Peer.LocalAddresses.Contains(p.Address));
                ImmutableInterlocked.Update(ref this.UnconnectedPeers, p => p.Union(peers));
            }
        }

        protected void ConnectToPeer(IPEndPoint endPoint, bool isTrusted = false)
        {
            endPoint = endPoint.Unmap();
            if (endPoint.Port == this.ListenerPort 
                && Peer.LocalAddresses.Contains(endPoint.Address))
            {
                return;
            }

            if (isTrusted)
            {
                this.TrustedIpAddresses.Add(endPoint.Address);
            }

            if (this.connectedAddresses.TryGetValue(endPoint.Address, out int count) 
                && count >= Peer.MaxConnectionsPerAddress)
            {
                return;
            }

            if (this.ConnectedPeers.Values.Contains(endPoint))
            {
                return;
            }

            ImmutableInterlocked.Update(
                ref this.connectingPeers, 
                p =>
                {
                    if ((p.Count >= ConnectingMax && !isTrusted) || p.Contains(endPoint))
                    {
                        return p;
                    }

                    Peer.TcpManagerActorRef.Tell(new Tcp.Connect(endPoint));
                    return p.Add(endPoint);
                });
        }

        protected abstract void NeedMorePeers(int count);

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Start start:
                    this.OnStart(
                        start.Port, 
                        start.WebSocketPort, 
                        start.MinDesiredConnections, 
                        start.MaxConnections);
                    break;
                case Timer _:
                    this.OnTimer();
                    break;
                case Peers peers:
                    this.AddPeers(peers.EndPoints);
                    break;
                case Connect connect:
                    this.ConnectToPeer(connect.EndPoint, connect.IsTrusted);
                    break;
                case WsConnected ws:
                    this.OnWsConnected(ws.Socket, ws.Remote, ws.Local);
                    break;
                case Tcp.Connected connected:
                    var remote = ((IPEndPoint)connected.RemoteAddress).Unmap();
                    var local = ((IPEndPoint)connected.LocalAddress).Unmap();
                    this.OnTcpConnected(remote, local);
                    break;
                case Tcp.Bound _:
                    this.tcpListenerActorRef = this.Sender;
                    break;
                case Tcp.CommandFailed commandFailed:
                    this.OnTcpCommandFailed(commandFailed.Cmd);
                    break;
                case Terminated terminated:
                    this.OnTerminated(terminated.ActorRef);
                    break;
            }
        }

        protected override void PostStop()
        {
            this.timer.CancelIfNotNull();
            this.webSocketHost?.Dispose();
            this.tcpListenerActorRef?.Tell(Tcp.Unbind.Instance);

            base.PostStop();
        }

        protected abstract Props ProtocolProps(object connection, IPEndPoint remote, IPEndPoint local);

        private static bool IsIntranetAddress(IPAddress address)
        {
            var data = address.MapToIPv4().GetAddressBytes();
            Array.Reverse(data);
            var value = data.ToUInt32(0);

            var isIntranetAddress = (value & 0xff000000) == 0x0a000000 
                || (value & 0xff000000) == 0x7f000000 
                || (value & 0xfff00000) == 0xac100000 
                || (value & 0xffff0000) == 0xc0a80000 
                || (value & 0xffff0000) == 0xa9fe0000;

            return isIntranetAddress;
        }

        private void OnStart(int port, int webSocketPort, int minDesiredConnections, int maxConnections)
        {
            this.ListenerPort = port;
            this.MinimumDesiredConnections = minDesiredConnections;
            this.MaxConnections = maxConnections;
            this.timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                initialMillisecondsDelay: 0, 
                millisecondsInterval: 5000, 
                receiver: Context.Self, 
                message: new Timer(), 
                sender: ActorRefs.NoSender);

            if ((port > 0 || webSocketPort > 0)
                && Peer.LocalAddresses.All(p => !p.IsIPv4MappedToIPv6 || Peer.IsIntranetAddress(p))
                && UPnP.Discover())
            {
                try
                {
                    Peer.LocalAddresses.Add(UPnP.GetExternalIP());
                    if (port > 0)
                    {
                        UPnP.ForwardPort(port, ProtocolType.Tcp, "NEO");
                    }

                    if (webSocketPort > 0)
                    {
                        UPnP.ForwardPort(webSocketPort, ProtocolType.Tcp, "NEO WebSocket");
                    }
                }
                catch
                {
                }
            }

            if (port > 0)
            {
                var tcpBindMessage = new Tcp.Bind(
                    handler: this.Self, 
                    localAddress: new IPEndPoint(IPAddress.Any, port), 
                    options: new[] { new Inet.SO.ReuseAddress(true) });

                TcpManagerActorRef.Tell(tcpBindMessage);
            }

            if (webSocketPort > 0)
            {
                this.webSocketHost = new WebHostBuilder()
                    .UseKestrel()
                    .UseUrls($"http://*:{webSocketPort}")
                    .Configure(app => app.UseWebSockets()
                    .Run(this.ProcessWebSocketAsync))
                    .Build();

                this.webSocketHost.Start();
            }
        }

        private void OnTcpConnected(IPEndPoint remote, IPEndPoint local)
        {
            ImmutableInterlocked.Update(ref this.connectingPeers, p => p.Remove(remote));
            if (this.MaxConnections != -1 
                && this.ConnectedPeers.Count >= this.MaxConnections 
                && !this.TrustedIpAddresses.Contains(remote.Address))
            {
                this.Sender.Tell(Tcp.Abort.Instance);
                return;
            }

            this.connectedAddresses.TryGetValue(remote.Address, out int count);
            if (count >= MaxConnectionsPerAddress)
            {
                this.Sender.Tell(Tcp.Abort.Instance);
            }
            else
            {
                this.connectedAddresses[remote.Address] = count + 1;
                var connectionProps = this.ProtocolProps(this.Sender, remote, local);
                var connection = Context.ActorOf(connectionProps, $"connection_{Guid.NewGuid()}");
                UntypedActor.Context.Watch(connection);

                this.Sender.Tell(new Tcp.Register(connection));
                this.ConnectedPeers.TryAdd(connection, remote);
            }
        }

        private void OnTcpCommandFailed(Tcp.Command cmd)
        {
            switch (cmd)
            {
                case Tcp.Connect connect:
                    ImmutableInterlocked.Update(
                        ref this.connectingPeers, 
                        p => p.Remove(((IPEndPoint)connect.RemoteAddress).Unmap()));
                    break;
            }
        }

        private void OnTerminated(IActorRef actorRef)
        {
            if (this.ConnectedPeers.TryRemove(actorRef, out IPEndPoint endPoint))
            {
                this.connectedAddresses.TryGetValue(endPoint.Address, out int count);
                if (count > 0)
                {
                    count--;
                }

                if (count == 0)
                {
                    this.connectedAddresses.Remove(endPoint.Address);
                }
                else
                {
                    this.connectedAddresses[endPoint.Address] = count;
                }
            }
        }

        private void OnTimer()
        {
            if (this.ConnectedPeers.Count >= this.MinimumDesiredConnections)
            {
                return;
            }

            if (this.UnconnectedPeers.Count == 0)
            {
                this.NeedMorePeers(this.MinimumDesiredConnections - this.ConnectedPeers.Count);
            }

            var endpoints = this.UnconnectedPeers
                .Take(this.MinimumDesiredConnections - this.ConnectedPeers.Count)
                .ToArray();

            ImmutableInterlocked.Update(ref this.UnconnectedPeers, p => p.Except(endpoints));
            foreach (var endpoint in endpoints)
            {
                this.ConnectToPeer(endpoint);
            }
        }

        private void OnWsConnected(WebSocket ws, IPEndPoint remote, IPEndPoint local)
        {
            this.connectedAddresses.TryGetValue(remote.Address, out int count);
            if (count >= MaxConnectionsPerAddress)
            {
                ws.Abort();
            }
            else
            {
                this.connectedAddresses[remote.Address] = count + 1;
                UntypedActor.Context.ActorOf(this.ProtocolProps(ws, remote, local), $"connection_{Guid.NewGuid()}");
            }
        }

        private async Task ProcessWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return;
            }

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            var remote = new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort);
            var local = new IPEndPoint(context.Connection.LocalIpAddress, context.Connection.LocalPort);

            base.Self.Tell(new WsConnected(ws, remote, local));
        }
        
        public class Start
        {
            public Start(int port, int webSocketPort, int minDesiredConnections, int maxConnections)
            {
                this.Port = port;
                this.WebSocketPort = webSocketPort;
                this.MinDesiredConnections = minDesiredConnections;
                this.MaxConnections = maxConnections;
            }

            public int Port { get; private set; }

            public int MaxConnections { get; private set; }

            public int MinDesiredConnections { get; private set; }

            public int WebSocketPort { get; private set; }
        }

        public class Peers
        {
            public Peers(IEnumerable<IPEndPoint> endpoints)
            {
                this.EndPoints = endpoints;
            }

            public IEnumerable<IPEndPoint> EndPoints { get; private set; }
        }

        public class Connect
        {
            public Connect(IPEndPoint endPoint, bool isTrusted = false)
            {
                this.EndPoint = endPoint;
                this.IsTrusted = isTrusted;
            }

            public IPEndPoint EndPoint { get; private set; }

            public bool IsTrusted { get; private set; }
        }

        private class Timer
        {
        }

        private class WsConnected
        {
            public WsConnected(WebSocket socket, IPEndPoint remote, IPEndPoint local)
            {
                this.Socket = socket;
                this.Remote = remote;
                this.Local = local;
            }

            public IPEndPoint Local { get; private set; }

            public WebSocket Socket { get; private set; }

            public IPEndPoint Remote { get; private set; }
        }
    }
}
