using System;

namespace Neo.IO.Data.LevelDB
{
    public class DB : IDisposable
    {
        private IntPtr handle;

        private DB(IntPtr handle)
        {
            this.handle = handle;
        }

        /// <summary>
        /// Return true if haven't got valid handle
        /// </summary>
        public bool IsDisposed => this.handle == IntPtr.Zero;

        public void Dispose()
        {
            if (this.handle != IntPtr.Zero)
            {
                Native.leveldb_close(this.handle);
                this.handle = IntPtr.Zero;
            }
        }

        public void Delete(WriteOptions options, Slice key)
        {
            IntPtr error;
            Native.leveldb_delete(this.handle, options.handle, key.buffer, (UIntPtr)key.buffer.Length, out error);
            NativeHelper.CheckError(error);
        }

        public Slice Get(ReadOptions options, Slice key)
        {
            UIntPtr length;
            IntPtr error;
            IntPtr value = Native.leveldb_get(
                this.handle, 
                options.handle, 
                key.buffer, 
                (UIntPtr)key.buffer.Length, 
                out length, 
                out error);

            try
            {
                NativeHelper.CheckError(error);
                if (value == IntPtr.Zero)
                {
                    throw new LevelDBException("not found");
                }

                return new Slice(value, length);
            }
            finally
            {
                if (value != IntPtr.Zero)
                {
                    Native.leveldb_free(value);
                }
            }
        }

        public Snapshot GetSnapshot() =>
            new Snapshot(this.handle);

        public Iterator NewIterator(ReadOptions options) =>
            new Iterator(Native.leveldb_create_iterator(this.handle, options.handle));        

        public static DB Open(string name) => DB.Open(name, Options.Default);        

        public static DB Open(string name, Options options)
        {
            IntPtr error;

            var handle = Native.leveldb_open(options.handle, name, out error);
            NativeHelper.CheckError(error);

            return new DB(handle);
        }

        public void Put(WriteOptions options, Slice key, Slice value)
        {
            IntPtr error;
            Native.leveldb_put(
                this.handle, 
                options.handle, 
                key.buffer, 
                (UIntPtr)key.buffer.Length, 
                value.buffer, 
                (UIntPtr)value.buffer.Length, 
                out error);

            NativeHelper.CheckError(error);
        }

        public bool TryGet(ReadOptions options, Slice key, out Slice value)
        {
            UIntPtr length;
            IntPtr error;
            var valueFromDb = Native.leveldb_get(
                this.handle, 
                options.handle, 
                key.buffer, 
                (UIntPtr)key.buffer.Length, 
                out length, 
                out error);

            if (error != IntPtr.Zero)
            {
                Native.leveldb_free(error);
                value = default(Slice);
                return false;
            }

            if (valueFromDb == IntPtr.Zero)
            {
                value = default(Slice);
                return false;
            }

            value = new Slice(valueFromDb, length);
            Native.leveldb_free(valueFromDb);
            return true;
        }

        public void Write(WriteOptions options, WriteBatch write_batch)
        {
            // There's a bug in .Net Core.
            // When calling DB.Write(), it will throw LevelDBException sometimes.
            // But when you try to catch the exception, the bug disappears.
            // We shall remove the "try...catch" clause when Microsoft fix the bug.
            byte retry = 0;
            while (true)
            {
                try
                {
                    IntPtr error;
                    Native.leveldb_write(this.handle, options.handle, write_batch.handle, out error);
                    NativeHelper.CheckError(error);
                    break;
                }
                catch (LevelDBException ex)
                {
                    if (++retry >= 4)
                    {
                        throw;
                    }

                    System.IO.File.AppendAllText("leveldb.log", ex.Message + "\r\n");
                }
            }
        }
    }
}
