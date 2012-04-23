using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LevelDb {
    static class Native {
        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_open(IntPtr options, string name, out IntPtr errptr);

        [DllImport("leveldb")]
        static internal extern void leveldb_close(IntPtr db);

        [DllImport("leveldb")]
        static internal extern void leveldb_put(IntPtr db, IntPtr options, byte[] key, int keylen, byte[] val, int vallen, out IntPtr errptr);

        [DllImport("leveldb")]
        static internal extern void leveldb_delete(IntPtr db, IntPtr options, byte[] key, int keylen, out IntPtr errptr);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_get(IntPtr db, IntPtr options, byte[] key, int keylen, out int vallen, out IntPtr errptr);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_create_iterator(IntPtr db, IntPtr options);

        [DllImport("leveldb")]
        static internal extern void leveldb_iter_destroy(IntPtr iter);

        [DllImport("leveldb")]
        static internal extern byte leveldb_iter_valid(IntPtr iter);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_iter_key(IntPtr db, out int keylen);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_iter_value(IntPtr db, out int valuelen);

        [DllImport("leveldb")]
        static internal extern void leveldb_iter_seek_to_first(IntPtr iter);

        [DllImport("leveldb")]
        static internal extern void leveldb_iter_next(IntPtr iter);

        [DllImport("leveldb")]
        static internal extern void leveldb_iter_get_error(IntPtr iter, out IntPtr errptr);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_writeoptions_create();

        [DllImport("leveldb")]
        static internal extern void leveldb_writeoptions_destroy(IntPtr options);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_readoptions_create();

        [DllImport("leveldb")]
        static internal extern void leveldb_readoptions_set_fill_cache(IntPtr options, byte create);

        [DllImport("leveldb")]
        static internal extern void leveldb_readoptions_destroy(IntPtr options);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_options_create();

        [DllImport("leveldb")]
        static internal extern void leveldb_options_destroy(IntPtr options);

        [DllImport("leveldb")]
        static internal extern void leveldb_options_set_create_if_missing(IntPtr options, byte create);

        [DllImport("leveldb")]
        static internal extern void leveldb_options_set_write_buffer_size(IntPtr options, int size);

        [DllImport("leveldb")]
        static internal extern void leveldb_options_set_max_open_files(IntPtr options, int count);

        [DllImport("leveldb")]
        static internal extern void leveldb_options_set_block_size(IntPtr options, int size);

        [DllImport("leveldb")]
        static internal extern void leveldb_options_set_block_restart_interval(IntPtr options, int interval);

        [DllImport("libc")]
        static internal extern void free(IntPtr val);

        [DllImport("leveldb")]
        static internal extern IntPtr leveldb_writebatch_create();

        [DllImport("leveldb")]
        static internal extern void leveldb_writebatch_destroy(IntPtr batch);

        [DllImport("leveldb")]
        static internal extern void leveldb_writebatch_put(IntPtr db, byte[] key, int keylen, byte[] val, int vallen);

        [DllImport("leveldb")]
        static internal extern void leveldb_writebatch_delete(IntPtr db, byte[] key, int keylen);

        [DllImport("leveldb")]
        static internal extern void leveldb_write(IntPtr db, IntPtr options, IntPtr batch, out IntPtr errptr);

        static internal void CheckError(IntPtr err) {
            if (err != IntPtr.Zero)  {
                var str = Marshal.PtrToStringAnsi(err);
                free(err);
                throw new Exception("Error: " + str);
            }

        }
    }
}
