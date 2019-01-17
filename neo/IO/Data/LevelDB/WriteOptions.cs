using System;

namespace Neo.IO.Data.LevelDB
{
    public class WriteOptions
    {
        public static readonly WriteOptions Default = new WriteOptions();
        internal readonly IntPtr handle = Native.leveldb_writeoptions_create();

        ~WriteOptions()
        {
            Native.leveldb_writeoptions_destroy(this.handle);
        }

        public bool Sync
        {
            set
            {
                Native.leveldb_writeoptions_set_sync(this.handle, value);
            }
        }
    }
}
