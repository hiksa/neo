using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using System.Collections;

namespace Neo.IO.Actors
{
    internal abstract class PriorityMailbox : MailboxType, IProducesMessageQueue<PriorityMessageQueue>
    {
        public PriorityMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        public override IMessageQueue Create(IActorRef owner, ActorSystem system) =>
            new PriorityMessageQueue(this.ShallDrop, this.IsHighPriority);

        protected virtual bool IsHighPriority(object message) => false;

        protected virtual bool ShallDrop(object message, IEnumerable queue) => false;
    }
}
