using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using Akka.Actor;
using Akka.IO;

namespace Neo.Network.P2P
{
    public abstract class Connection : UntypedActor
    {
        private readonly IActorRef tcpActorRef;
        private readonly WebSocket ws;

        private ICancelable timer;
        private bool disconnected = false;

        protected Connection(object connection, IPEndPoint remote, IPEndPoint local)
        {
            this.Remote = remote;
            this.Local = local;
            this.timer = Context.System.Scheduler
                .ScheduleTellOnceCancelable(TimeSpan.FromSeconds(10), this.Self, Timer.Instance, ActorRefs.NoSender);

            switch (connection)
            {
                case IActorRef tcp:
                    this.tcpActorRef = tcp;
                    break;
                case WebSocket ws:
                    this.ws = ws;
                    this.WsReceive();
                    break;
            }
        }
        
        public IPEndPoint Remote { get; }

        public IPEndPoint Local { get; }

        public abstract int ListenerPort { get; }

        public void Disconnect(bool abort = false)
        {
            this.disconnected = true;
            if (this.tcpActorRef != null)
            {
                var closeConnectionMessage = abort 
                    ? (Tcp.CloseCommand)Tcp.Abort.Instance 
                    : Tcp.Close.Instance;

                this.tcpActorRef.Tell(closeConnectionMessage);
            }
            else
            {
                this.ws.Abort();
            }

            Context.Stop(this.Self);
        }

        protected virtual void OnAck()
        {
        }

        protected abstract void OnData(ByteString data);

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Timer _:
                    this.Disconnect(true);
                    break;
                case Ack _:
                    this.OnAck();
                    break;
                case Tcp.Received received:
                    this.OnReceived(received.Data);
                    break;
                case Tcp.ConnectionClosed _:
                    Context.Stop(this.Self);
                    break;
            }
        }

        protected override void PostStop()
        {
            if (!this.disconnected)
            {
                this.tcpActorRef?.Tell(Tcp.Close.Instance);
            }

            this.timer.CancelIfNotNull();
            this.ws?.Dispose();
            base.PostStop();
        }

        protected void SendData(ByteString data)
        {
            if (this.tcpActorRef != null)
            {
                var tcpWriteMessage = Tcp.Write.Create(data, Ack.Instance);
                this.tcpActorRef.Tell(tcpWriteMessage);
            }
            else
            {
                var segment = new ArraySegment<byte>(data.ToArray());
                this.ws
                    .SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None)
                    .PipeTo(
                        recipient: this.Self,
                        success: () => Ack.Instance,
                        failure: ex => new Tcp.ErrorClosed(ex.Message));
            }
        }

        private void OnReceived(ByteString data)
        {
            this.timer.CancelIfNotNull();
            this.timer = Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromMinutes(1), 
                this.Self, 
                Timer.Instance,
                ActorRefs.NoSender);

            try
            {
                this.OnData(data);
            }
            catch
            {
                this.Disconnect(true);
            }
        }

        private void WsReceive()
        {
            var buffer = new byte[512];
            var segment = new ArraySegment<byte>(buffer);
            this.ws.ReceiveAsync(segment, CancellationToken.None).PipeTo(
                this.Self,
                success: p =>
                {
                    switch (p.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            return new Tcp.Received(ByteString.FromBytes(buffer, 0, p.Count));
                        case WebSocketMessageType.Close:
                            return Tcp.PeerClosed.Instance;
                        default:
                            this.ws.Abort();
                            return Tcp.Aborted.Instance;
                    }
                },
                failure: ex => new Tcp.ErrorClosed(ex.Message));
        }

        internal class Timer
        {
            public static Timer Instance { get; } = new Timer();
        }

        internal class Ack : Tcp.Event
        {
            public static Ack Instance { get; } = new Ack();
        }
    }
}
