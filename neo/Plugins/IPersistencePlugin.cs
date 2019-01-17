﻿using Neo.Persistence;
using System.Collections.Generic;
using static Neo.Ledger.Blockchain;

namespace Neo.Plugins
{
    public interface IPersistencePlugin
    {
        void OnPersist(Snapshot snapshot, IReadOnlyList<ApplicationExecuted> allResults);
    }
}
