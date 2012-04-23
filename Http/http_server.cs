using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace HttpServer {
    /*
     * This is the implemntation of HttpServer. 
     *
     * For API documentation, see http_api.cs.
     */
    public sealed partial class HttpServer 
    {
        internal readonly object                _lock           = new object();
        readonly int                            _port;
        readonly int                            _backlog;
        readonly SynchronizationContext/*?*/    _cx;
        readonly List<_Connection>              _connections    = new List<_Connection>();

        Socket                                  _listensock;
        bool                                    _isdisposed;
        Timer                                   _timer;

        //
        // Dispatch a callback either in the configured synchronization context or in the thread pool
        // depending on the user's preference.
        //
        internal void _DispatchCallback(Action cb) {
            if (_cx == null) ThreadPool.QueueUserWorkItem(_ => cb());
            else             _cx.Post(_ => cb(), null);
        }

        //
        // Log an error
        //
        void ev_error(string s, Exception e = null) {
            if (e != null)
                Console.Error.WriteLine("Error: {0}\n{1}", s, e);
            else
                Console.Error.WriteLine("Error: {0}");
        }

        void ev_debug(string s) {
            //Console.WriteLine("LOG: {0}", s);
        }

        void ev_debug(string s, params object[] ps) {
            //Console.WriteLine("LOG: " + string.Format(s, ps));
        }

        //
        // Stop server + clean up
        //
        void _Dispose() {
            lock (_lock) {
                if (_isdisposed) return;
                _isdisposed = true;

                if (_timer != null) {
                    _timer.Dispose();
                    _timer = null;
                }

                if (_listensock != null) {
                    try { _listensock.Close(); } catch { }
                    _listensock = null;
                }

                if (_connections.Count > 0) {
                    foreach (var connection in _connections)
                        connection.Dispose();
                    _connections.Clear();
                }
            }
        }

        //
        // Listen/Accept loop
        //
        void _Start() {
            lock (_lock) {
                if (_isdisposed)         throw new ObjectDisposedException("HttpServer");
                if (_listensock != null) throw new InvalidOperationException("HttpServer was already started");
                _listensock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _listensock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listensock.Bind(new IPEndPoint(IPAddress.Any, _port));
                _listensock.Listen(_backlog);
                ev_debug("BOUND PORT " + BoundPort);
                _timer = new Timer(ev_poll, null, 1000, 1000);
                _AcceptLoop();
            }
        }

        void ev_poll(object _) {
            lock (_lock) {
                if (_isdisposed) return;
                ev_debug("--------------------------------------------------------------------------------------------");
                foreach (var connection in _connections)
                    connection._PrintDiag();
            }
        }

        int _BoundPort { get {
            lock (_lock) {
                if (_isdisposed)         throw new ObjectDisposedException("HttpServer");
                if (_listensock == null) throw new InvalidOperationException("HttpServer must be started before reading BoundPort");
                return ((IPEndPoint)_listensock.LocalEndPoint).Port;
            }
        } } 

        void _AcceptLoop() {
            lock (_lock) {
                if (_isdisposed) return;
                _listensock.BeginAccept(ev_accept, null);
            }
        }

        void ev_accept(IAsyncResult r) {
            Socket socket;
            try {
                lock (_lock) {
                    if (_isdisposed) return;
                    ev_debug("accepting connection");
                    socket = _listensock.EndAccept(r);
                    socket.NoDelay = true;
                    _AcceptLoop();
                    var connection = new _Connection(this, socket);
                    _connections.Add(connection);
                }
            } catch (IOException) {
                _AcceptLoop();
            } catch (SocketException) {
                _AcceptLoop();
            } catch (ObjectDisposedException) {
                return;
            } catch (Exception e) {
                ev_error("Error accepting connection", e);
            }
        }

        void ev_connectionfailed(_Connection connection) {
            lock (_lock) {
                if (_isdisposed) return;
                _connections.Remove(connection);
            }
        }

        bool ev_need100continue(HttpTransaction tx) {
            HttpHandlerDelegate evh;
            lock (_lock) {
                if (_isdisposed) return true;
                evh = HandleExpect100Continue;
            }
            if (evh != null) { 
                _DispatchCallback(() => evh(tx));
                return true;
            }
            return false;
        }

        void ev_request_ready(HttpTransaction tx) {
            HttpHandlerDelegate evh;
            lock (_lock) {
                if (_isdisposed) return;
                evh = HandleRequest;
            }
            if (evh == null) { 
                tx.Response.Respond(HttpStatusCode.NotFound);
            } else {
                _DispatchCallback(() => evh(tx));
            }
        }

        void ev_need100continue() {
        }

        // 
        // Represents an HTTP client connection
        //
        internal class _Connection : IDisposable {
            enum _State {
                RequestHeaders,           // (read)  read request headers
                Send100ContinueUserWait,  // (wait)  wait for user to send 100-continue
                Send100Continue,          // (write) send 100 continue 
                RequestBodyFull,          // (read)  read request body
                RequestBodyDump,          // (read)  dump request body
                RequestBodyMultiPart,     // (read)  read request body
                RequestBodyUserWait,      // (wait)  wait for user to be ready for request body read
                RequestBodyUserRead,      // (read)  read request body
                ResponseHeadersUserWait,  // (wait)  wait for 
                SendResponseHeaders,      // (write) send response headers
                ResponseBodyUserWait,     // (wait)  wait for response buffer from user
                SendResponseBody,         // (write) write user's buffer
                SendResponseBodyLast,     // (write) write user's last buffer
                Failure,                  // (write) failure response, then close socket
            }

            enum _HeaderState {
                FirstLine,              // within the first line (e.g. 'GET /foo HTTP/1.1')
                FirstLineR,             // after the \r on the first line
                HeaderLine,             // within a header line
                HeaderLineR,            // after the first \r on a header line
                LastHeaderLineR,        // after \r\n\r at the end of the headers
            }

            readonly HttpServer _server;
            readonly Socket     _socket;

            internal EndPoint   _remoteendpoint;
            internal bool       _ishttp10;

            _State              _state              = _State.RequestHeaders;
            HttpTransaction     _tx;
            bool                _isconnectionclose;
            bool                _isdisposed;

            // Read buffer
            byte[]              _readbuf            = new byte[4096];
            int                 _readoff            = 0;
            int                 _readcount          = 0;

            // for _State.RequestHeaders
            _HeaderState        _headerstate        = _HeaderState.FirstLine;
            MemoryStream        _linems             = new MemoryStream();
            string              _firstline;
            List<string>        _headerlines        = new List<string>();

            // for _State.Failure, _State.Send100Continue, _State.SendResponseHeaders
            int                 _writeoff;
            int                 _writecount;
            byte[]              _writebytes;

            // for _State.RequestBody*
            long                _requestbody_left;

            // for _State.RequestBodyFull
            MemoryStream        _requestbody_full;

            // for _State.RequestBodyMultiPart
            HttpMultiPartFormDataHandler _requestbody_multipart;

            // for _State.RequestBodyUser*
            bool                _requestbody_streaming_started;
            RequestBodyBuffer   _requestbody_buffer;
            Action<HttpBuffer>  _requestbody_user_cb;

            // for _State.*ResponseBody*
            long                _responsebody_left;
            bool                _responsebody_streaming_started;
            ResponseBodyBuffer  _responsebody_buffer;
            Action<HttpBuffer>  _responsebody_user_cb;
            Action              _responsebody_done_cb;

            internal _Connection(HttpServer server, Socket socket) {
                _server         = server;
                _socket         = socket;
                _remoteendpoint = socket.RemoteEndPoint;
                _server.ev_debug("create connection " + _remoteendpoint);
                lock (_server._lock)
                    _HandleCurrentState();
            }

            void _HandleCurrentState() {
                _server.ev_debug("handle state! " + _state);
                switch (_state)     {
                    case _State.RequestHeaders:             _BeginRead();                        break;
                    case _State.Failure:                    _BeginWrite();                       break;
                    case _State.Send100ContinueUserWait:    /* user will initiate next i/o */    break;
                    case _State.Send100Continue:            _BeginWrite();                       break;
                    case _State.RequestBodyFull:            _BeginRead();                        break;
                    case _State.RequestBodyDump:            _BeginRead();                        break;
                    case _State.RequestBodyMultiPart:       _BeginRead();                        break;
                    case _State.RequestBodyUserWait:        /* user will initiate next i/o */    break;
                    case _State.RequestBodyUserRead:        _BeginRead();                        break;
                    case _State.ResponseHeadersUserWait:    /* user will initiate next i/o */    break;
                    case _State.SendResponseHeaders:        _BeginWrite();                       break;
                    case _State.ResponseBodyUserWait:       /* user will initiate next i/o */    break;
                    case _State.SendResponseBody:           _BeginWrite();                       break;
                    case _State.SendResponseBodyLast:       _BeginWrite();                       break;
                    default:                                throw new InvalidOperationException();
                }
            }

            void _BeginWrite() {
                if (_writecount == 0) {
                    ev_writedone();
                    return;
                }
                try {
                    _server.ev_debug("begin write " + _writecount + " bytes");
                    _socket.BeginSend(_writebytes, _writeoff, _writecount, SocketFlags.Partial, ar => {
                        lock (_server._lock) {
                            if (_isdisposed) return;
                            try {
                                int written = _socket.EndSend(ar);
                                _writeoff   += written;
                                _writecount -= written;
                                if (_writecount != 0) {
                                    _BeginWrite();
                                } else {
                                    ev_writedone();
                                }
                            } catch (IOException) {
                                Dispose();
                            } catch (SocketException) {
                                Dispose();
                            } catch (ObjectDisposedException) {
                                Dispose();
                            } catch (Exception e) {
                                _server.ev_error("Error writing connection", e);
                                Dispose();
                            }
                        }
                    }, null);
                } catch (IOException) {
                    Dispose();
                } catch (SocketException) {
                    Dispose();
                } catch (ObjectDisposedException) {
                    Dispose();
                } catch (Exception e) {
                    _server.ev_error("Error writing connection", e);
                    Dispose();
                }
            }

            void ev_writedone() {
                if (_state == _State.Failure) {
                    Dispose(); 

                } else if (_state == _State.Send100Continue) {
                    ev_requestbodyready();
                    _HandleCurrentState();

                } else if (_state == _State.SendResponseBody) {
                    _state  = _State.ResponseBodyUserWait;
                    var cb  = _responsebody_user_cb;
                    var buf = _responsebody_buffer;
                    _server._DispatchCallback(() => cb(buf));

                } else if (_state == _State.SendResponseBodyLast) {
                    // clear out all state
                    _tx                             = null;
                    _readoff                        = 0;
                    _readcount                      = 0;
                    _readbuf                        = new byte[4096];
                    _readoff                        = 0;
                    _readcount                      = 0;
                    _headerstate                    = _HeaderState.FirstLine;
                    _linems                         = new MemoryStream();
                    _firstline                      = null;
                    _headerlines                    = new List<string>();
                    _writeoff                       = 0;
                    _writecount                     = 0;
                    _writebytes                     = null;
                    _requestbody_left               = 0;
                    _requestbody_full               = null;
                    _requestbody_multipart          = null;
                    _requestbody_streaming_started  = false;
                    _requestbody_buffer             = null;
                    _requestbody_user_cb            = null;
                    _responsebody_streaming_started = false;
                    _responsebody_buffer            = null;
                    _responsebody_user_cb           = null;
                    _responsebody_done_cb           = null;   
                    _responsebody_left              = -1;
                    _state                          = _State.RequestHeaders;

                    if (_isconnectionclose || _ishttp10) {
                        Dispose();
                    } else {
                        _HandleCurrentState();
                    }

                } else if (_state == _State.SendResponseHeaders) {
                    _state = _State.ResponseBodyUserWait;
                    _server._DispatchCallback(() => _responsebody_user_cb(_responsebody_buffer));

                } else {
                    throw new InvalidOperationException();
                }
            }

            void _BeginRead() {
                lock (_server._lock) {
                    if (_isdisposed) return;
                    if (_readcount > 0) {       // previous buffer handler failed to consume whole buffer. 
                        ev_buffer();
                        return;
                    } 
                    try {
                        _socket.BeginReceive(_readbuf, 0, 4096, SocketFlags.Partial, ar => {
                            lock (_server._lock) {
                                if (_isdisposed) return;
                                try {
                                    _readoff   = 0;
                                    _readcount = _socket.EndReceive(ar);
                                    if (_readcount == 0) {          // eof
                                        Dispose();
                                        return;
                                    }
                                    ev_buffer();
                                } catch (IOException) {
                                    Dispose();
                                } catch (SocketException) {
                                    Dispose();
                                } catch (ObjectDisposedException) {
                                    Dispose();
                                } catch (Exception e) {
                                    _server.ev_error("Error reading connection", e);
                                    Dispose();
                                }
                            }
                        } , null);
                    } catch (IOException) {
                        Dispose();
                    } catch (SocketException) {
                        Dispose();
                    } catch (ObjectDisposedException) {
                        Dispose();
                    } catch (Exception e) {
                        _server.ev_error("Error reading from connection", e);
                        Dispose();
                    }
                }
            }

            void ev_buffer() {
                if (_state == _State.RequestHeaders)
                    ev_requestheaders_buffer();
                else if (_state == _State.RequestBodyDump) 
                    ev_requestbody_dump_buffer();
                else if (_state == _State.RequestBodyFull) 
                    ev_requestbody_full_buffer();
                else if (_state == _State.RequestBodyMultiPart) 
                    ev_requestbody_multipart_buffer();
                else if (_state == _State.RequestBodyUserRead) 
                    ev_requestbody_user_buffer();
                else
                    throw new InvalidOperationException();      // shouldn't happen
                _HandleCurrentState();
            }


            void _Fail(HttpStatusCode code) {
                var response      = string.Format("HTTP/1.1 {0} {1}\r\nContent-Length: 0\r\n\r\n", (int)code, _Utils.Stringify(code));
                var responsebytes = Encoding.UTF8.GetBytes(response);

                _state      = _State.Failure;
                _writeoff   = 0;
                _writecount = responsebytes.Length;
                _writebytes = responsebytes;
            }

            void ev_requestbody_user_buffer() {
                int toread = (int)Math.Min(_requestbody_left, _readcount);
                _requestbody_buffer.Buffer       = _readbuf;
                _requestbody_buffer.Offset       = _readoff;
                _requestbody_buffer.Count        = toread;
                _requestbody_buffer.IsLastBuffer = _requestbody_left == toread;
                _requestbody_left -= toread;
                _readoff          += toread;
                _readcount        -= toread;
                _state = _State.RequestBodyUserWait;
                _server._DispatchCallback(() => _requestbody_user_cb(_requestbody_buffer));
            }

            void ev_requestbody_multipart_buffer() {
                int toread = (int)Math.Min(_requestbody_left, _readcount);
                _requestbody_multipart.HandleData(_tx.Request, _readbuf, _readoff, toread);
                _requestbody_left -= toread;
                _readoff          += toread;
                _readcount        -= toread;
                if (_requestbody_left == 0) {
                    _state = _State.ResponseHeadersUserWait;
                    ev_requestready();
                }
            }

            void ev_requestbody_dump_buffer() {
                int toread = (int)Math.Min(_requestbody_left, _readcount);
                _requestbody_left -= toread;
                _readoff          += toread;
                _readcount        -= toread;
                if (_requestbody_left == 0) {
                    _state = _State.SendResponseHeaders;
                }
            }

            void ev_requestbody_full_buffer() {
                int toread = (int)Math.Min(_requestbody_left, _readcount);
                _requestbody_full.Write(_readbuf, _readoff, toread);
                _requestbody_left -= toread;
                _readoff          += toread;
                _readcount        -= toread;
                if (_requestbody_left == 0) {
                    _state = _State.ResponseHeadersUserWait;
                    _tx.Request.SetFormBody(Encoding.UTF8.GetString(_requestbody_full.ToArray()));
                    ev_requestready();
                }
            }

            void ev_requestheaders_buffer() {
                bool done = false;
                for (; !done && _readcount > 0; _readoff++, _readcount--) {
                    switch (_headerstate) {
                        case _HeaderState.FirstLine: 
                            if (_readbuf[_readoff] == (byte)'\r') {
                                _headerstate = _HeaderState.FirstLineR;
                            } else {
                                _linems.WriteByte(_readbuf[_readoff]);
                            }
                            break;

                        case _HeaderState.FirstLineR: 
                            if (_readbuf[_readoff] == (byte)'\n') {
                                _firstline = Encoding.UTF8.GetString(_linems.ToArray());
                                _linems = new MemoryStream();
                                _headerstate = _HeaderState.HeaderLine;
                            } else {
                                _linems.WriteByte((byte)'\r');
                                _linems.WriteByte(_readbuf[_readoff]);
                                _headerstate = _HeaderState.FirstLine;
                            }
                            break;

                        case _HeaderState.HeaderLine:
                            if (_readbuf[_readoff] == (byte)'\r') {
                                if (_linems.Length == 0)
                                    _headerstate = _HeaderState.LastHeaderLineR;
                                else
                                    _headerstate = _HeaderState.HeaderLineR;
                            } else {
                                _linems.WriteByte(_readbuf[_readoff]);
                            }
                            break;

                        case _HeaderState.HeaderLineR:
                            if (_readbuf[_readoff] == (byte)'\n') {
                                _headerlines.Add(Encoding.UTF8.GetString(_linems.ToArray()));
                                _linems = new MemoryStream();
                                _headerstate = _HeaderState.HeaderLine;
                            } else {
                                _linems.WriteByte((byte)'\r');
                                _linems.WriteByte(_readbuf[_readoff]);
                                _headerstate = _HeaderState.HeaderLine;
                            }
                            break;

                        case _HeaderState.LastHeaderLineR:
                            if (_readbuf[_readoff] == (byte)'\n') {
                                ev_headersdone();
                                done = true;
                            } else {
                                _linems.WriteByte((byte)'\r');
                                _linems.WriteByte(_readbuf[_readoff]);
                                _headerstate = _HeaderState.HeaderLine;
                            }
                            break;

                        default:
                            throw new InvalidOperationException();
                    }
                }
            }

            void ev_headersdone() {
                _server.ev_debug("======================================================================");
                _server.ev_debug(_firstline);
                foreach (var headerline in _headerlines)
                    _server.ev_debug(headerline);
                _server.ev_debug("--------------------------------------");

                string[] splits = _firstline.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                if (splits.Length != 3) {
                    _Fail(HttpStatusCode.HttpVersionNotSupported);
                    return;
                }
                string httpversion = splits[2].Trim().ToUpper();
                if (!httpversion.StartsWith("HTTP/")) {
                    _Fail(HttpStatusCode.HttpVersionNotSupported);
                    return;
                }
                if (httpversion.EndsWith("1.0")) {
                    _ishttp10 = true;
                }

                HttpMethod meth;
                switch (splits[0].Trim().ToUpper()) {
                    case "HEAD":   meth = HttpMethod.Head;               break;
                    case "GET":    meth = HttpMethod.Get;                break;
                    case "PUT":    meth = HttpMethod.Put;                break;
                    case "DELETE": meth = HttpMethod.Delete;             break;
                    case "POST":   meth = HttpMethod.Post;               break;
                    default:       _Fail(HttpStatusCode.NotImplemented); return;
                }
                var request  = new HttpRequest(this, meth, splits[1]);
                var response = new HttpResponse(this, meth);
                _tx = new HttpTransaction(_server, this, request, response);

                bool needs100continue = false;

                foreach (var line in _headerlines) {
                    int colon_index = line.IndexOf(':');
                    if (colon_index < 0) {
                        _Fail(HttpStatusCode.BadRequest);
                        return;
                    }
                    string key = line.Substring(0, colon_index).Trim();
                    string value = line.Substring(colon_index + 1).Trim();
                    request.Headers.SetHeader(key, value);

                    if (key == "Connection" && value.ToLower() == "close") {
                        _isconnectionclose = true;
                    }

                    if (key == "Expect" && value.ToLower() == "100-continue") {
                        needs100continue = true;
                    }
                }

                if (needs100continue) {
                    ev_need100continue();
                } else {
                    ev_requestbodyready();
                }
            }

            void ev_need100continue() {
                if (!_server.ev_need100continue(_tx)) {
                    _Send100Continue();
                } else {
                    _state = _State.Send100ContinueUserWait;
                }
            }

            void _Send100Continue() {
                _state      = _State.Send100Continue;
                _writebytes = Encoding.UTF8.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                _writeoff   = 0;
                _writecount = _writebytes.Length;
            }

            internal void UserSend100Continue() {
                lock (_server._lock) {
                    if (_server._isdisposed) return;
                    if (_state != _State.Send100ContinueUserWait)
                        throw new InvalidOperationException("tried to send 100 continue at an invalid time");
                    _Send100Continue();
                    _HandleCurrentState();
                }
            }

            void ev_requestbodyready() {
                //
                // There are four strategies for handling the request body
                //
                // (1) Content-Length is 0: Nothing to do
                //
                // (2) Content-Length > 0 + Content-Type is application/x-www-form-urlencoded: Read it now.
                //
                // (3) Content-Length > 0 + Content-Type is application/x-www-urlencoded: Read it now.
                //
                // (4) Content-Length > 0 + Content-Type is something else: Let the user stream the request body.
                //
                // (5) Transfer-Encoding is chunked: Fail with NotImplemented (for now--may support this in future)
                //

                _server.ev_debug("request body ready");

                if (_tx.Request.Headers.IsChunkedEncoding) {
                    _Fail(HttpStatusCode.NotImplemented);               // case (4) -- fail
                    return;
                }

                var contentlength = _tx.Request.Headers.ContentLength ?? 0;
                if (contentlength == 0) {
                    _state = _State.ResponseHeadersUserWait;
                    _server.ev_debug("    ==> case 1 (nothing to do)");
                    ev_requestready();                                    // case (1) -- nothing to do
                } else {
                    if (_tx.Request.Method == HttpMethod.Post && 
                        _tx.Request.Headers.ContentType != null && 
                        _tx.Request.Headers.ContentType.Contains("multipart/form-data")) {

                        _server.ev_debug("    ==> case 1 (multipart)");
                        var boundary           = _ParseBoundary(_tx.Request.Headers.ContentType);
                        _requestbody_multipart = new HttpMultiPartFormDataHandler(boundary, _tx.Request.Headers.ContentEncoding);
                        _requestbody_left      = contentlength;
                        _state                 = _State.RequestBodyMultiPart;     // case (2) -- read request body now

                    } else if (_tx.Request.Method == HttpMethod.Post && 
                        _tx.Request.Headers.ContentType != null && 
                        _tx.Request.Headers.ContentType.Contains("application/x-www-form-urlencoded")) {

                        _server.ev_debug("    ==> case 1 (urlencoded)");

                        _requestbody_full = new MemoryStream();
                        _requestbody_left = contentlength;
                        _state            = _State.RequestBodyFull;     // case (2) -- read request body now

                    } else {
                        _server.ev_debug("    ==> case 1 (userstream)");
                        _state = _State.RequestBodyUserWait;            // case (3) -- let user stream request body
                        _requestbody_left = contentlength;
                        _requestbody_streaming_started = false;
                        _tx.Request.SetStreamingRequestBody(true);
                        ev_requestready();
                    }
                }
            }

            static string _ParseBoundary (string ct) {
                if (ct == null) return null;
                int start = ct.IndexOf ("boundary=");
                if (start < 1) return null;
                return ct.Substring (start + "boundary=".Length);
            }

            internal class RequestBodyBuffer : HttpBuffer {
                readonly _Connection _connection;
                internal RequestBodyBuffer(_Connection connection) { _connection = connection; } 
                public override void Complete() { _connection._RequestBodyBufferComplete(); }
            }

            void _RequestBodyBufferComplete() {
                lock (_server._lock) {
                    if (_isdisposed) return;    // this is not the right place to throw

                    if (_state != _State.RequestBodyUserWait)
                        throw new InvalidOperationException("request is not waiting for buffer complete");

                    if (_requestbody_left > 0) {
                        _state = _State.RequestBodyUserRead;
                        _HandleCurrentState();
                    } else {
                        _state = _State.ResponseHeadersUserWait; 
                        _requestbody_buffer  = null;
                        _requestbody_user_cb = null;
                        _HandleCurrentState();
                    }
                }
            }

            internal void BeginStreamingRequestBody(HttpRequest request, Action<HttpBuffer> cb_buffer) { 
                lock (_server._lock) {
                    if (_isdisposed) return;    // this is not the right place to throw
                    if (_state != _State.RequestBodyUserWait)
                        throw new InvalidOperationException("invalid state for request body streaming");
                    if (_requestbody_streaming_started)
                        throw new InvalidOperationException("request body can only be streamed once");
                    if (_tx == null || _tx.Request != request)
                        throw new InvalidOperationException("this request object is not active");

                    _requestbody_streaming_started = true;
                    _requestbody_buffer            = new RequestBodyBuffer(this);
                    _requestbody_user_cb           = cb_buffer;
                    _state                         = _State.RequestBodyUserRead;
                    _HandleCurrentState();
                }
            }

            internal void _BeginResponse(HttpResponse response, HttpStatusCode status, Action<HttpBuffer> ev_buffer, Action/*?*/ ev_done = null)  { 
                lock (_server._lock) {
                    if (_isdisposed) return;    // this is not the right place to throw
                    if (_state != _State.ResponseHeadersUserWait && _state != _State.RequestBodyUserWait)
                        throw new InvalidOperationException("invalid state for response body streaming (" + _state + ")");
                    if (_responsebody_streaming_started)
                        throw new InvalidOperationException("request body can only be streamed once");
                    if (_tx == null || _tx.Response != response)
                        throw new InvalidOperationException("this request object is not active");

                    _server.ev_debug("begin response");
                    _responsebody_buffer            = new ResponseBodyBuffer(this);
                    _responsebody_user_cb           = ev_buffer;
                    if (_responsebody_done_cb != null) throw new InvalidOperationException("resources may have leaked");
                    _responsebody_done_cb           = ev_done;

                    if (!response.Headers.ContentLength.HasValue && !_ishttp10) {
                        response.Headers.SetChunkedEncoding();
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.AppendFormat("HTTP/{0} {1} {2}\r\n", (_ishttp10?"1.0":"1.1"), (int)status, _Utils.Stringify(status));
                    response.Headers.Write(sb, response.Cookies.Values);

                    _server.ev_debug("{0}", sb.ToString());
                    _server.ev_debug("--------------------------------------");

                    _responsebody_left = response.Headers.ContentLength ?? -1;
                    _writebytes        = Encoding.UTF8.GetBytes(sb.ToString());
                    _writecount        = _writebytes.Length;
                    _writeoff          = 0;
                    if (_state == _State.RequestBodyUserWait) {         // if we are still waiting for the user to read the request body, dump it
                        _state = _State.RequestBodyDump;
                    } else {
                        _state = _State.SendResponseHeaders;
                    }
                    _HandleCurrentState();
                }
            }

            internal class ResponseBodyBuffer : HttpBuffer {
                readonly _Connection _connection;
                internal ResponseBodyBuffer(_Connection connection) { _connection = connection; } 
                public override void Complete() { _connection._ResponseBodyBufferComplete(); }
            }

            void _ResponseBodyBufferComplete() {
                lock (_server._lock) {
                    if (_isdisposed) return;    // this is not the right place to throw

                    if (_state != _State.ResponseBodyUserWait)
                        throw new InvalidOperationException("response is not waiting for buffer complete (state=" + _state + ")");

                    bool chunked = false;

                    if (_responsebody_left != -1) {
                        if (_responsebody_buffer.Count > _responsebody_left) {
                            Dispose();
                            throw new InvalidOperationException("sent more data than content length");
                        }
                        if (_responsebody_buffer.IsLastBuffer && _responsebody_buffer.Count != _responsebody_left) {
                            Dispose();
                            throw new InvalidOperationException("sent last buffer before reaching content length");
                        }
                        _responsebody_left -= _responsebody_buffer.Count;
                    } else if (!_ishttp10) {
                        chunked = true;
                    }

                    if (chunked) {
                        // if we have no content length + are not http 1.0, then chunk it
                        MemoryStream ms = new MemoryStream();
                        byte[] before = Encoding.ASCII.GetBytes(String.Format("{0:X}\r\n", _responsebody_buffer.Count));
                        byte[] after = Encoding.ASCII.GetBytes("\r\n");
                        ms.Write(before, 0, before.Length);
                        ms.Write(_responsebody_buffer.Buffer, _responsebody_buffer.Offset, _responsebody_buffer.Count);
                        ms.Write(after, 0, after.Length);
                        if (_responsebody_buffer.IsLastBuffer) {
                            byte[] trailer = Encoding.ASCII.GetBytes("0\r\n\r\n");
                            ms.Write(trailer, 0, trailer.Length);
                            _FireResponseBodyDone();
                        }
                        //_server.ev_debug(Encoding.UTF8.GetString(ms.ToArray()));
                        //_server.ev_debug("--------------------------------------");
                        _writebytes = ms.ToArray();
                        _writeoff   = 0;
                        _writecount = _writebytes.Length;
                    } else {
                        //_server.ev_debug(Encoding.UTF8.GetString(_responsebody_buffer.Buffer, _responsebody_buffer.Offset, _responsebody_buffer.Count));
                        //_server.ev_debug("--------------------------------------");
                        // not chunked; just write the data
                        _writebytes = _responsebody_buffer.Buffer;
                        _writeoff   = _responsebody_buffer.Offset;
                        _writecount = _responsebody_buffer.Count;
                    }

                    if (_responsebody_buffer.IsLastBuffer) {
                        _FireResponseBodyDone();
                        _state      = _State.SendResponseBodyLast;
                    } else {
                        _state      = _State.SendResponseBody;
                    }

                    _HandleCurrentState();
                }
            }

            void _FireResponseBodyDone() {
                var cb = _responsebody_done_cb;
                _responsebody_done_cb = null;
                if (cb != null) {
                    _server._DispatchCallback(() => cb());
                }
            }

            void ev_requestready() {
                _server.ev_request_ready(_tx);
            }

            internal void _PrintDiag() {
                _server.ev_debug("Connection [" + _remoteendpoint + "] state=" + _state);
            }

            public void Dispose() {
                lock (_server._lock) {
                    if (_isdisposed) return;
                    _server.ev_debug("dispose connection");
                    _isdisposed = true;
                    _FireResponseBodyDone();
                    if (_tx != null) {
                        _tx.Cancel();
                        _tx = null;
                    }
                    try { _socket.Close(); } catch { }
                    _server.ev_connectionfailed(this);
                }
            }
        }
    }

    public sealed partial class HttpTransaction {
        readonly HttpServer             _server;
        readonly HttpServer._Connection _connection;
        readonly HttpResponse           _response;
        readonly HttpRequest            _request;
        bool                            _iscanceled;

        internal HttpTransaction(HttpServer server, HttpServer._Connection connection, HttpRequest request, HttpResponse response) {
            _server     = server;
            _request    = request;
            _response   = response;
            _connection = connection;
        }

        bool _IsCanceled { get { 
            lock (_server._lock) 
                return _IsCanceled; 
        } }

        void _Cancel() {
            lock (_server._lock) {
                if (_iscanceled) return;
                _iscanceled = true;
                OnCanceled();
                _connection.Dispose();
            }
        }

        void OnCanceled() {
            var evh = Canceled;
            if (evh != null)
                _server._DispatchCallback(() => evh(this));
        }
    }

    public sealed partial class HttpRequest
    {
        readonly HttpServer._Connection    _connection;
        readonly HttpHeaders               _headers             = new HttpHeaders();

        string                             _rawpath;
        string                             _path;
        HttpMethod                         _method;             
        bool                               _streamingrequestbody;       // indicates that request body can be streamed by user
        string                             _formbody;                   // raw request body if content type is x-www-urlencoded
        HttpDataDictionary                 _querydata;                  // (lazy) params from query portion of url
        HttpDataDictionary                 _postdata;                   // (lazy) params from x-www-urlencoded request body
        HttpDataDictionary                 _data;                       // (lazy) params from x-www-urlencoded request body
        Dictionary<string, UploadedFile>   _files;                      // (lazy) files from x-multipart-form-data               
        HttpDataDictionary                 _cookies;

        internal HttpRequest(HttpServer._Connection connection, HttpMethod method, string rawpath) {
            _connection = connection;
            _method     = method;
            _rawpath    = rawpath;
            int iof     = rawpath.IndexOf('?');
            _path       = iof >= 0 ? rawpath.Substring(0, iof) : rawpath;
        }

        public void Send100Continue() {
            _connection.UserSend100Continue();
        }

        HttpDataDictionary _Data { get {
            if (_data == null) {
                _data = new HttpDataDictionary();
                _data.Children.Add(_QueryData);
                _data.Children.Add(_PostData);
            }
            return _data;
        } }

        HttpDataDictionary _PostData { get {
            if (_postdata == null) {
                _postdata = new HttpDataDictionary();
                if (_formbody != null)
                    _ParseUrlEncoded(_postdata, _formbody);
            }
            return _postdata;
        } }

        HttpDataDictionary _QueryData { get {
            if (_querydata == null) {
                _querydata = new HttpDataDictionary();
                int iof = RawPath.IndexOf('?');
                if (iof >= 0) {
                    string qs = RawPath.Substring(iof + 1);
                    _ParseUrlEncoded(_querydata, qs);
                }
            }
            return _querydata;
        } }

        public Dictionary<string,UploadedFile> _Files { get {
            if (_files == null)
                _files = new Dictionary<string, UploadedFile>();
            return _files;
        } }

        private void _ParseUrlEncoded(HttpDataDictionary d, string qs) {
            string[] parts = qs.Split('&');
            foreach (string p in parts) {
                string[] kv = p.Split('=');
                if (kv.Length == 2) {
                    string key   = _Utils.UrlDecode(kv[0]);
                    string value = _Utils.UrlDecode(kv[1]);
                    d.Set(key, new UnsafeString(value));
                }
            }
        }

        internal void SetFormBody(string formbody) {
            _formbody = formbody;
        }

        internal void SetStreamingRequestBody(bool streaming) {
            _streamingrequestbody = streaming;
        }

        HttpDataDictionary _Cookies { get {
            if (_cookies == null) {
                string value;
                if (!_headers.TryGetValue ("Cookie", out value))
                    _cookies = new HttpDataDictionary ();
                _cookies = HttpCookie.FromHeader (value);
            }
            return _cookies;
        } }
    }

    public sealed partial class HttpResponse {
        readonly HttpServer._Connection         _connection;
        readonly HttpMethod                     _method;             
        readonly HttpHeaders                    _headers = new HttpHeaders();
        Dictionary<string, HttpCookie>          _cookies;

        internal HttpResponse(HttpServer._Connection connection, HttpMethod method) {
            _connection = connection;
            _method     = method;
            if (_method == HttpMethod.Head || _method == HttpMethod.Delete)
                _headers.ContentLength = 0;
        }

        void _Respond(HttpStatusCode status, string body) { 
            _Respond(status, Encoding.UTF8.GetBytes(body));
        }

        void _Respond(HttpStatusCode status) {
            _Respond(status, new byte[0]);
        }

        void _Respond(HttpStatusCode status, Stream body) {
            Headers.ContentLength = body.Length;
            byte[] buf = new byte[32768];
            BeginResponse(HttpStatusCode.OK, 
                          ev_buffer: responsebuf => {
                              responsebuf.Buffer = buf;
                              responsebuf.Offset = 0;
                              responsebuf.Count  = body.Read(buf, 0, buf.Length);
                              if (body.Position == body.Length)
                                  responsebuf.IsLastBuffer = true;
                              responsebuf.Complete();
                          }, 
                          ev_done: () => {
                              try { body.Close(); } catch { }
                          });
        }

        void _Respond(HttpStatusCode status, byte[] body) { 
            Headers.ContentLength = body.Length;
            _connection._BeginResponse(this, status, buf => {
                buf.Buffer       = body;
                buf.Offset       = 0;
                buf.Count        = body.Length;
                buf.IsLastBuffer = true;
                buf.Complete();
            });
        }

        Dictionary<string, HttpCookie> _Cookies { get {
            if (_cookies == null) _cookies = new Dictionary<string, HttpCookie>();
            return _cookies;
        } }
    }
}
