using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.IO;

namespace HttpServer {
    //
    // Http Server
    //
    // Usage example:
    //
    // using (var server = new HttpServer(port: 8000, cx: ...)) {
    //     server.HandleRequest += ev_request;
    //     server.Start();
    //
    //     ... pump eventloop or something ...
    // }
    //
    // void ev_request(HttpTransaction tx) { ... }
    //
    public sealed partial class HttpServer : IDisposable {
        //
        // Constructor 
        //
        // HttpServer can operate with or without a synchronization context. 
        // If a synchronization context is provided, then events and callbacks
        // are delivered on that synchronization context. Otherwise, events and 
        // callbacks are delivered using the threadpool.
        //
        // If you set port to 0, an arbitrary port will be assigned in Start().
        // You can find out what that port is using the BoundPort property.
        //
        public HttpServer(
            int                      port        = 0, 
            int                      backlog     = 100, 
            IPAddress                addr        = null,
            SynchronizationContext   cx          = null
        )
        { 
            _backlog = backlog;
            _port    = port; 
            _cx      = cx; 
        }

        // 
        // events
        //
        
        //
        // hook up to this to handle incoming requests.
        //
        public event HttpHandlerDelegate HandleRequest;

        //
        // If you want to control when 100-continue is sent for expect: 100-continue, then
        // hook up to this event and call Request.Send100Continue() when you're ready
        //
        public event HttpHandlerDelegate HandleExpect100Continue;

        //
        // Once you've hooked up to events, call this to start the server
        //
        // This method will throw an exception if it fails to set up the listener socket.
        //
        public void Start()     { _Start(); }

        //
        // use this after Start() returns to find out what port the server has bound.
        //
        public int BoundPort    { get { return _BoundPort; } }

        //
        // dispose the server when you're done with it to prevent resource leaks.
        //
        public void Dispose()   { _Dispose(); }
    }

    //
    // an Http Transaction is a single request/response pair.
    //
    public sealed partial class HttpTransaction {
        public HttpServer      Server          { get { return _server;   } }
        public HttpRequest     Request         { get { return _request;  } }
        public HttpResponse    Response        { get { return _response; } }

        //
        // A transaction is canceled if the socket associated with that connection
        // is closed for any reason while the transaction is in progress.
        //
        // API consumers that do substantial amounts of work per transaction may
        // want to hook up to this event so that work can be canceled when 
        // HTTP clients disappear.
        //
        public event HttpTransactionCanceledDelegate Canceled;        
        public bool            IsCanceled      { get { lock (_server._lock) return _iscanceled; } }

        //
        // Cancel this transaction and tear down its TCP connection immediately.
        //
        public void Cancel()                    { _Cancel(); }
    }

    public sealed partial class HttpRequest {
        //
        // returns the method associated with the request
        //
        public HttpMethod              Method                 { get { return _method;                     } }

        //
        // returns the path associated with the request with query arguments removed.
        //
        public string                  Path                   { get { return _path;                       } }

        // 
        // returns the raw path as it appeared in the HTTP request
        //
        public string                  RawPath                { get { return _rawpath;                    } }

        //
        // returns the request headers
        //
        public HttpHeaders             Headers                { get { return _headers;                    } }

        //
        // indicates whether or not the request is from an HTTP 1.0 client
        //
        public bool                    IsHttp10               { get { return _connection._ishttp10;       } }

        //
        // returns the remote endpoint associated with the HTTP client
        //
        public EndPoint                RemoteEndPoint         { get { return _connection._remoteendpoint; } }

        //
        // Data from the query portion of the request URI
        //
        public HttpDataDictionary       QueryData             { get { return _QueryData; } }

        //
        // Data from the body of an x-www-urlencoded POST request, if any
        //
        public HttpDataDictionary       PostData              { get { return _PostData;  } }

        //
        // Data from the URI + PostData combined 
        //
        public HttpDataDictionary       Data                  { get { return _Data;      } }

        //
        // Uploaded files from multipart form data.
        //
        public IDictionary<string,UploadedFile> Files         { get { return _Files;     } }

        //
        // Cookies associated with this request
        //
        public HttpDataDictionary               Cookies       { get { return _Cookies;   } }

        //
        // returns true if the request body is to be handled in a streaming fashion
        //
        // for requests with an empty body, this will return false.
        //
        // for application/x-www-urlencoded requests, this will return false and the request body will
        // be automatically decoded and used to populate the 'Form' property. 
        //
        // for other requests, this will return true. In this case, the request body can be accessed 
        // by calling BeginStreamingRequestBody.
        //
        public bool                    IsStreamingRequestBody { get { return _streamingrequestbody;       } }

        //
        // Begin streaming the request body. 
        //
        // For each chunk of data received, cb_buffer will be invoked.
        //
        // For each call to cb_buffer, you must call Complete() to free the buffer and initiate the next
        // read operation. This allows the consumer of the request stream to control the flow of data.
        //
        public void BeginStreamingRequestBody(Action<HttpBuffer> cb_buffer) { _connection.BeginStreamingRequestBody(this, cb_buffer); }
    }

    //
    // Represents the response to an HTTP request.
    //
    // There are two ways to respond to an HTTP request
    //
    // (1) Set any desired headers, then call one of the Respond() overloads on HttpResponse.
    //
    // (2) Set any desired headers, then Call BeginResponse() on HttpResponse to begin a streaming response.
    //
    public sealed partial class HttpResponse {
        //
        // Headers allows you to access + modify the response headers
        //
        public HttpHeaders             Headers         { get { return _headers;        } }

        //
        // To send a response all at once, use one of these Respond overloads
        //
        public void Respond(HttpStatusCode status, string body, params object[] ps) { _Respond(status, string.Format(body,ps)); }
        public void Respond(HttpStatusCode status, string body) { _Respond(status, body);            }
        public void Respond(HttpStatusCode status, byte[] body) { _Respond(status, body);            }
        public void Respond(HttpStatusCode status, Stream body) { _Respond(status, body);            }
        public void Respond(HttpStatusCode status)              { _Respond(status);                  }

        public void Respond(string body, params object[] ps)    { _Respond(HttpStatusCode.OK, string.Format(body, ps)); }
        public void Respond(string body)                        { _Respond(HttpStatusCode.OK, body); }
        public void Respond(byte[] body)                        { _Respond(HttpStatusCode.OK, body); }
        public void Respond(Stream body)                        { _Respond(HttpStatusCode.OK, body); }
        public void Respond()                                   { _Respond(HttpStatusCode.OK);       }

        //
        // Convenience method for setting headers
        //
        public void SetHeader(string name, string value)        { Headers.SetHeader(name, value); }

        //
        // To send a streaming respond, call BeginResponse.
        //
        // Once the client is ready to consume buffers, cb_buffer will be invoked with an HttpBuffer object.
        //
        // Fill in the Buffer, Offset, and Count fields on this object, then call HttpBuffer.Complete() to pass
        // the buffer to the client.
        //
        // When you have filled the last buffer, set the IsLastBuffer property to true. This will complete the 
        // HTTP response.
        //
        // Streaming responses are always sent using chunked transfer encoding.
        //
        public void BeginResponse(HttpStatusCode status, 
                                  Action<HttpBuffer> ev_buffer,
                                  Action             ev_done = null)  { _connection._BeginResponse(this, status, ev_buffer, ev_done); }

        public void BeginResponse(Action<HttpBuffer> ev_buffer,
                                  Action             ev_done = null)  { BeginResponse(HttpStatusCode.OK, ev_buffer, ev_done); }

        //
        // Cookies associated with this response
        // 
        public IDictionary<string,HttpCookie>   Cookies       { get { return _Cookies;   } }

        //
        // Set a cookie using a cookie object
        // 
        public void SetCookie (string name, HttpCookie cookie) {
            Cookies[name] = cookie;
        }

        //
        // Set a cookie using parameters
        // 
        public HttpCookie SetCookie (string name, string value, string domain = null, DateTime? expires = null, TimeSpan? max_age = null) {
            if (name  == null)                        throw new ArgumentNullException("name");
            if (value == null)                        throw new ArgumentNullException("value");
            if (expires.HasValue && max_age.HasValue) throw new ArgumentException("setting expires and max age doesn't make sense");
            var cookie = new HttpCookie(name, value) { Domain  = domain, };
            if (expires.HasValue) cookie.Expires = expires.Value;
            if (max_age.HasValue) cookie.Expires = DateTime.Now + max_age.Value;
            SetCookie(name, cookie);
            return cookie;
        }

        //
        // Remove a cookie
        // 
        public void RemoveCookie(string name) {
            SetCookie(name, new HttpCookie (name, "") { Expires = DateTime.Now.AddYears(-1) });
        }
    }

    public abstract class HttpBuffer {
        public byte[]           Buffer            { get; set; }
        public int              Offset            { get; set; }
        public int              Count             { get; set; }
        public bool             IsLastBuffer      { get; set; }

        public abstract void Complete();
    }

    public delegate void HttpHandlerDelegate(HttpTransaction tx);

    public delegate void HttpTransactionCanceledDelegate(HttpTransaction tx);

    public enum HttpMethod { 
        Get, 
        Post, 
        Put, 
        Head, 
        Delete 
    }

    public enum HttpStatusCode {
        Continue                     = 100,
        SwitchingProtocols           = 101,
        OK                           = 200,
        Created                      = 201,
        Accepted                     = 202,
        NonAuthoritativeInformation  = 203,
        NoContent                    = 204,
        ResetContent                 = 205,
        PartialContent               = 206,
        MultipleChoices              = 300,
        Ambiguous                    = 300,
        MovedPermanently             = 301,
        Moved                        = 301,
        Found                        = 302,
        Redirect                     = 302,
        SeeOther                     = 303,
        RedirectMethod               = 303,
        NotModified                  = 304,
        UseProxy                     = 305,
        Unused                       = 306,
        TemporaryRedirect            = 307,
        RedirectKeepVerb             = 307,
        BadRequest                   = 400,
        Unauthorized                 = 401,
        PaymentRequired              = 402,
        Forbidden                    = 403,
        NotFound                     = 404,
        MethodNotAllowed             = 405,
        NotAcceptable                = 406,
        ProxyAuthenticationRequired  = 407,
        RequestTimeout               = 408,
        Conflict                     = 409,
        Gone                         = 410,
        LengthRequired               = 411,
        PreconditionFailed           = 412,
        RequestEntityTooLarge        = 413,
        RequestUriTooLong            = 414,
        UnsupportedMediaType         = 415,
        RequestedRangeNotSatisfiable = 416,
        ExpectationFailed            = 417,
        InternalServerError          = 500,
        NotImplemented               = 501,
        BadGateway                   = 502,
        ServiceUnavailable           = 503,
        GatewayTimeout               = 504,
        HttpVersionNotSupported      = 505,
    }

    public sealed class HttpContentRange {
        public long     Offset          { get; set; }
        public long     Count           { get; set; }
        public long     TotalCount      { get; set; }

        public HttpContentRange(string s) {
            if (!s.StartsWith("bytes "))
                throw new FormatException();
            s = s.Substring(6);
            string[] splits = s.Split('/');
            string[] splits2 = splits[0].Split('-');
            if (splits[1] == "*")
                TotalCount = -1;
            else
                TotalCount = long.Parse(splits[1]);
            Offset = long.Parse(splits2[0]);
            Count = long.Parse(splits2[1]) + 1 - Offset;
        }

        public HttpContentRange(long offset, long count, long total) {
            Offset     = offset;
            Count      = count;
            TotalCount = total;
        }

        public override string ToString() {
            return string.Format("bytes {0}-{1}/{2}", Offset, Offset + Count - 1, TotalCount < 0 ? "*" : (TotalCount-1).ToString());
        }
    }

    public sealed class HttpRange {
        public long     Offset          { get; set; }
        public long?    Count           { get; set; }

        internal HttpRange(string s) {
            if (!s.StartsWith("bytes=")) 
                throw new FormatException();
            s = s.Substring(6);
            int dash = s.IndexOf('-');
            if (dash < 0) {
                Offset = 0;
                Count = long.Parse(s);
                return;
            } else {
                long from = long.Parse(s.Substring(0, dash));
                string sto = s.Substring(dash + 1);
                if (sto.Trim().Length != 0) {
                    long to = long.Parse(s.Substring(dash + 1));
                    Count = to + 1 - from;
                }
                Offset = from;
            }
        }

        public override string ToString() {
            if (Count == null)
                return string.Format("bytes={0}-", Offset);
            else
                return string.Format("bytes={0}-{1}", Offset, Offset + Count - 1);
        }
    }

    public sealed partial class UploadedFile : IDisposable {
        public string           Name            { get; internal set; }
        public string           ContentType     { get; internal set; }
        public Stream           Contents        { get; internal set; }
        public long             Length          { get { return Contents.Length; } }

        public void Dispose() { if (Contents != null) { Contents.Close (); Contents = null; } }
        ~UploadedFile() { Dispose (); }
    }

    public class HttpException : Exception {
        public HttpException (string error) : base(error) { }
        public HttpException (string error, Exception cause) : base (error, cause) { }
    }
}
