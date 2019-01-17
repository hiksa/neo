using Akka.Configuration;
using Neo.IO.Actors;
using Neo.Network.P2P.Payloads;

namespace Neo.Network.P2P
{
    internal class TaskManagerMailbox : PriorityMailbox
    {
        public TaskManagerMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case TaskManager.Register _:
                case TaskManager.RestartTasks _:
                    return true;
                case TaskManager.NewTasks tasks:
                    if (tasks.Payload.Type == InventoryType.Block || tasks.Payload.Type == InventoryType.Consensus)
                    {
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }
    }
}
