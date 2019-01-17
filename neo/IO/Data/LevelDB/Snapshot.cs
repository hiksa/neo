using System;

namespace Neo.IO.Data.LevelDB
{
    public class Snapshot : IDisposable
    {
        internal IntPtr db, handle;

        internal Snapshot(IntPtr db)
        {
            this.db = db;
            this.handle = Native.leveldb_create_snapshot(db);
        }

        public void Dispose()
        {
            if (this.handle != IntPtr.Zero)
            {
                Native.leveldb_release_snapshot(this.db, this.handle);
                this.handle = IntPtr.Zero;
            }
        }
    }
}
