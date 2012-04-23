using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LevelDb {
    public class Database : IDisposable, IEnumerable<KeyValuePair<byte[],byte[]>> {
        readonly IntPtr           _dbptr;
        readonly IntPtr           _getoptions;
        readonly IntPtr           _loadoptions;
        readonly IntPtr           _writeoptions;
        bool                      _isdisposed;

        public Database(string path) {
            IntPtr err = IntPtr.Zero;

            var options = Native.leveldb_options_create();
            try {
                Native.leveldb_options_set_max_open_files(options, 32768);
                Native.leveldb_options_set_write_buffer_size(options, 1024 * 1024 * 64);
                Native.leveldb_options_set_block_size(options, 32768);
                Native.leveldb_options_set_create_if_missing(options, 1);
                var dbptr = Native.leveldb_open(options, path, out err);
                Native.CheckError(err);
                _dbptr = dbptr;
            } finally {
                Native.leveldb_options_destroy(options);
            }


            // for get, use default options
            _getoptions = Native.leveldb_readoptions_create();

            // for load, don't fill cache
            _loadoptions = Native.leveldb_readoptions_create();  // use 
            Native.leveldb_readoptions_set_fill_cache(_loadoptions, 0);

            // for write, use default options (no sync)
            _writeoptions = Native.leveldb_writeoptions_create();
        }

        public void Put(byte[] key, byte[] val) {
            IntPtr err = IntPtr.Zero;
            Native.leveldb_put(_dbptr, _writeoptions, key, key.Length, val, val.Length, out err);
            Native.CheckError(err);
        }

        public void Delete(byte[] key) {
            IntPtr err = IntPtr.Zero;
            Native.leveldb_delete(_dbptr, _writeoptions, key, key.Length, out err);
            Native.CheckError(err);
        }

        public bool TryGetValue(byte[] key, out byte[] val) {
            IntPtr err = IntPtr.Zero;
            int vallen;
            IntPtr valptr = IntPtr.Zero;
            valptr = Native.leveldb_get(_dbptr, _getoptions, key, key.Length, out vallen, out err);
            try {
                Native.CheckError(err);
                if (valptr != IntPtr.Zero) {
                    val = new byte[vallen];
                    Marshal.Copy(valptr, val, 0, vallen);
                    return true;
                } else {
                    val = null;
                    return false;
                }
            } finally {
                if (valptr != IntPtr.Zero)
                    Native.free(valptr);
            }
        }

        public int Count() {
            IntPtr iter = Native.leveldb_create_iterator(_dbptr, _loadoptions);
            if (iter == IntPtr.Zero) throw new Exception("failed to create iterator");
            try {
                int count = 0;
                for (Native.leveldb_iter_seek_to_first(iter); 0 != Native.leveldb_iter_valid(iter); Native.leveldb_iter_next(iter)) {
                    count++;
                }
                IntPtr err = IntPtr.Zero;
                Native.leveldb_iter_get_error(iter, out err);
                Native.CheckError(err);
                return count;
            } finally {
                Native.leveldb_iter_destroy(iter);
            }
        }

        public IEnumerator<KeyValuePair<byte[],byte[]>> GetEnumerator() { 
            IntPtr iter = Native.leveldb_create_iterator(_dbptr, _loadoptions);
            if (iter == IntPtr.Zero) throw new Exception("failed to create iterator");
            try {
                for (Native.leveldb_iter_seek_to_first(iter); 0 != Native.leveldb_iter_valid(iter); Native.leveldb_iter_next(iter)) {
                    int keylen;
                    int vallen;

                    IntPtr keyptr = Native.leveldb_iter_key(iter, out keylen);
                    IntPtr valptr = Native.leveldb_iter_value(iter, out vallen);

                    if (keyptr == IntPtr.Zero) throw new Exception("failed to get key");
                    if (keyptr == IntPtr.Zero) throw new Exception("failed to get val");

                    byte[] key = new byte[keylen];
                    byte[] val = new byte[vallen];

                    Marshal.Copy(keyptr, key, 0, keylen);
                    Marshal.Copy(valptr, val, 0, vallen);

                    yield return new KeyValuePair<byte[], byte[]>(key, val);
                }
                IntPtr err = IntPtr.Zero;
                Native.leveldb_iter_get_error(iter, out err);
                Native.CheckError(err);
            } finally {
                Native.leveldb_iter_destroy(iter);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { 
            foreach (var i in this)
                yield return i;
        }

        public void Write(WriteBatch batch) {
            try {
                if (batch._batchptr == IntPtr.Zero) throw new ObjectDisposedException("LevelDbWriteBatch");
                IntPtr err = IntPtr.Zero;
                Native.leveldb_write(_dbptr, _writeoptions, batch._batchptr, out err);
                //Console.WriteLine("batch: {0} bytes/rec, {1} bytes total", batch._totalbytes/batch._count, batch._totalbytes);
                Native.CheckError(err);
            } finally {
                batch.Dispose();
            }
        }

        public void Dispose() {
            if (_isdisposed) return;
            _isdisposed = true;
            Native.leveldb_readoptions_destroy(_getoptions);
            Native.leveldb_readoptions_destroy(_loadoptions);
            Native.leveldb_writeoptions_destroy(_writeoptions);
            Native.leveldb_close(_dbptr);
        }

        ~Database() {
            Dispose();
        }
    }

    public class WriteBatch : IDisposable {
        internal IntPtr _batchptr;

        internal int _totalbytes;
        internal int _count;

        public WriteBatch() {
            _batchptr = Native.leveldb_writebatch_create();
        }

        public void Put(byte[] key, byte[] val) {
            if (_batchptr == IntPtr.Zero) throw new InvalidOperationException();
            Native.leveldb_writebatch_put(_batchptr, key, key.Length, val, val.Length);
            _totalbytes += val.Length + key.Length;
            _count++;
        }

        public void Delete(byte[] key) {
            if (_batchptr == IntPtr.Zero) throw new InvalidOperationException();
            Native.leveldb_writebatch_delete(_batchptr, key, key.Length);
        }

        public void Dispose() {
            if (_batchptr != IntPtr.Zero) {
                Native.leveldb_writebatch_destroy(_batchptr);
                _batchptr = IntPtr.Zero;
            }
        }
        ~WriteBatch() {
            Dispose();
        }
    }

    public class Exception : System.Exception {
        public Exception(string s) : base(s) { }
    }
}


