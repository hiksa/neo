using System;

namespace Neo.IO.Data.LevelDB
{
    public class Iterator : IDisposable
    {
        private IntPtr handle;

        internal Iterator(IntPtr handle)
        {
            this.handle = handle;
        }

        private void CheckError()
        {
            IntPtr error;
            Native.leveldb_iter_get_error(this.handle, out error);
            NativeHelper.CheckError(error);
        }

        public void Dispose()
        {
            if (this.handle != IntPtr.Zero)
            {
                Native.leveldb_iter_destroy(this.handle);
                this.handle = IntPtr.Zero;
            }
        }

        public Slice Key()
        {
            UIntPtr length;
            IntPtr key = Native.leveldb_iter_key(this.handle, out length);
            this.CheckError();
            return new Slice(key, length);
        }

        public void Next()
        {
            Native.leveldb_iter_next(this.handle);
            this.CheckError();
        }

        public void Prev()
        {
            Native.leveldb_iter_prev(this.handle);
            this.CheckError();
        }

        public void Seek(Slice target)
        {
            Native.leveldb_iter_seek(this.handle, target.buffer, (UIntPtr)target.buffer.Length);
        }

        public void SeekToFirst()
        {
            Native.leveldb_iter_seek_to_first(this.handle);
        }

        public void SeekToLast()
        {
            Native.leveldb_iter_seek_to_last(this.handle);
        }

        public bool Valid()
        {
            return Native.leveldb_iter_valid(this.handle);
        }

        public Slice Value()
        {
            UIntPtr length;
            IntPtr value = Native.leveldb_iter_value(this.handle, out length);
            this.CheckError();
            return new Slice(value, length);
        }
    }
}
