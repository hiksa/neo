using System;

namespace Neo.IO.Data.LevelDB
{
    public class Options
    {
        public static readonly Options Default = new Options();
        internal readonly IntPtr handle = Native.leveldb_options_create();

        ~Options()
        {
            Native.leveldb_options_destroy(this.handle);
        }

        public bool CreateIfMissing
        {
            set
            {
                Native.leveldb_options_set_create_if_missing(this.handle, value);
            }
        }

        public bool ErrorIfExists
        {
            set
            {
                Native.leveldb_options_set_error_if_exists(this.handle, value);
            }
        }

        public bool ParanoidChecks
        {
            set
            {
                Native.leveldb_options_set_paranoid_checks(this.handle, value);
            }
        }

        public int WriteBufferSize
        {
            set
            {
                Native.leveldb_options_set_write_buffer_size(this.handle, (UIntPtr)value);
            }
        }

        public int MaxOpenFiles
        {
            set
            {
                Native.leveldb_options_set_max_open_files(this.handle, value);
            }
        }

        public int BlockSize
        {
            set
            {
                Native.leveldb_options_set_block_size(this.handle, (UIntPtr)value);
            }
        }

        public int BlockRestartInterval
        {
            set
            {
                Native.leveldb_options_set_block_restart_interval(this.handle, value);
            }
        }

        public CompressionType Compression
        {
            set
            {
                Native.leveldb_options_set_compression(this.handle, value);
            }
        }

        public IntPtr FilterPolicy
        {
            set
            {
                Native.leveldb_options_set_filter_policy(this.handle, value);
            }
        }
    }
}
