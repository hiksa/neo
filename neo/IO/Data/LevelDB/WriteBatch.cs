using System;

namespace Neo.IO.Data.LevelDB
{
    public class WriteBatch
    {
        internal readonly IntPtr handle = Native.leveldb_writebatch_create();

        ~WriteBatch()
        {
            Native.leveldb_writebatch_destroy(this.handle);
        }

        public void Clear()
        {
            Native.leveldb_writebatch_clear(this.handle);
        }

        public void Delete(Slice key)
        {
            Native.leveldb_writebatch_delete(this.handle, key.buffer, (UIntPtr)key.buffer.Length);
        }

        public void Put(Slice key, Slice value)
        {
            Native.leveldb_writebatch_put(
                this.handle, 
                key.buffer, 
                (UIntPtr)key.buffer.Length, 
                value.buffer, 
                (UIntPtr)value.buffer.Length);
        }
    }
}
