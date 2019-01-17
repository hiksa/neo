using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Neo.IO.Actors
{
    internal class PriorityMessageQueue : IMessageQueue, IUnboundedMessageQueueSemantics
    {
        private readonly ConcurrentQueue<Envelope> high = new ConcurrentQueue<Envelope>();
        private readonly ConcurrentQueue<Envelope> low = new ConcurrentQueue<Envelope>();
        private readonly Func<object, IEnumerable, bool> dropper;
        private readonly Func<object, bool> priorityGenerator;
        private int idle = 1;

        public PriorityMessageQueue(Func<object, IEnumerable, bool> dropper, Func<object, bool> priorityGenerator)
        {
            this.dropper = dropper;
            this.priorityGenerator = priorityGenerator;
        }

        public bool HasMessages => !this.high.IsEmpty || !this.low.IsEmpty;

        public int Count => this.high.Count + this.low.Count;

        public void CleanUp(IActorRef owner, IMessageQueue deadletters)
        {
        }

        public void Enqueue(IActorRef receiver, Envelope envelope)
        {
            Interlocked.Increment(ref this.idle);
            if (envelope.Message is Idle)
            {
                return;
            }

            if (this.dropper(envelope.Message, this.high.Concat(this.low).Select(p => p.Message)))
            {
                return;
            }

            var queue = this.priorityGenerator(envelope.Message) ? this.high : this.low;
            queue.Enqueue(envelope);
        }

        public bool TryDequeue(out Envelope envelope)
        {
            if (this.high.TryDequeue(out envelope))
            {
                return true;
            }

            if (this.low.TryDequeue(out envelope))
            {
                return true;
            }

            if (Interlocked.Exchange(ref this.idle, 0) > 0)
            {
                envelope = new Envelope(Idle.Instance, ActorRefs.NoSender);
                return true;
            }

            return false;
        }
    }
}
