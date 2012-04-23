using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace HttpServer {
    internal static class _Utils
    {
        public static string Stringify(HttpStatusCode code)
        {
            switch (code)
            {
                case HttpStatusCode.Accepted: return "Accepted";
                case HttpStatusCode.Ambiguous: return "Multiple Choices";
                case HttpStatusCode.BadGateway: return "Bad Gateway";
                case HttpStatusCode.BadRequest: return "Bad Request";
                case HttpStatusCode.Conflict: return "Conflict";
                case HttpStatusCode.Continue: return "Continue";
                case HttpStatusCode.Created: return "Created";
                case HttpStatusCode.ExpectationFailed: return "Expectation Failed";
                case HttpStatusCode.Forbidden: return "Forbidden";
                case HttpStatusCode.Found: return "Found";
                case HttpStatusCode.GatewayTimeout: return "Gateway Timeout";
                case HttpStatusCode.Gone: return "Gone";
                case HttpStatusCode.HttpVersionNotSupported: return "HTTP Version Not Supported";
                case HttpStatusCode.InternalServerError: return "Internal Server Error";
                case HttpStatusCode.LengthRequired: return "Length Required";
                case HttpStatusCode.MethodNotAllowed: return "Method Not Allowed";
                case HttpStatusCode.Moved: return "Moved";
                case HttpStatusCode.NoContent: return "No Content";
                case HttpStatusCode.NonAuthoritativeInformation: return "Non Authoritative Information";
                case HttpStatusCode.NotAcceptable: return "Not Acceptable";
                case HttpStatusCode.NotFound: return "Not Found";
                case HttpStatusCode.NotImplemented: return "Not Implemented";
                case HttpStatusCode.NotModified: return "Not Modified";
                case HttpStatusCode.OK: return "OK";
                case HttpStatusCode.PartialContent: return "Partial Content";
                case HttpStatusCode.PaymentRequired: return "Payment Required";
                case HttpStatusCode.PreconditionFailed: return "Precondition Failed";
                case HttpStatusCode.ProxyAuthenticationRequired: return "Proxy Authentication Required";
                case HttpStatusCode.RedirectKeepVerb: return "Redirect Keep Verb";
                case HttpStatusCode.RedirectMethod: return "Redirect Method";
                case HttpStatusCode.RequestedRangeNotSatisfiable: return "Requested Range Not Satisfiable";
                case HttpStatusCode.RequestEntityTooLarge: return "Request Entity Too Large";
                case HttpStatusCode.RequestTimeout: return "Request Timeout";
                case HttpStatusCode.RequestUriTooLong: return "Request URI Too Long";
                case HttpStatusCode.ResetContent: return "Reset Content";
                case HttpStatusCode.ServiceUnavailable: return "Service Unavailable";
                case HttpStatusCode.SwitchingProtocols: return "Switching Protocols";
                case HttpStatusCode.Unauthorized: return "Unauthorized";
                case HttpStatusCode.UnsupportedMediaType: return "Unsupported Media Type";
                case HttpStatusCode.Unused: return "Unused";
                case HttpStatusCode.UseProxy: return "Use Proxy";
                default: return code.ToString();
            }
        }

        class UrlDecoder
        {
            private int _bufferSize;
            private byte[] _byteBuffer;
            private char[] _charBuffer;
            private Encoding _encoding;
            private int _numBytes;
            private int _numChars;

            internal UrlDecoder(int bufferSize, Encoding encoding) {
                this._bufferSize = bufferSize;
                this._encoding = encoding;
                this._charBuffer = new char[bufferSize];
            }

            internal void AddByte(byte b) {
                if (this._byteBuffer == null) {
                    this._byteBuffer = new byte[this._bufferSize];
                }
                this._byteBuffer [this._numBytes++] = b;
            }

            internal void AddChar(char ch) {
                if (this._numBytes > 0) {
                    this.FlushBytes();
                }
                this._charBuffer [this._numChars++] = ch;
            }

            private void FlushBytes() {
                if (this._numBytes > 0) {
                    this._numChars += this._encoding.GetChars(this._byteBuffer, 0, this._numBytes, this._charBuffer, this._numChars);
                    this._numBytes = 0;
                }
            }

            internal string GetString() {
                if (this._numBytes > 0) {
                    this.FlushBytes();
                }
                if (this._numChars > 0) {
                    return new string(this._charBuffer, 0, this._numChars);
                }
                return string.Empty;
            }
        }

        static int HexToInt(char h)
        {
            if ((h >= '0') && (h <= '9'))
                return (h - '0');
            if ((h >= 'a') && (h <= 'f'))
                return ((h - 'a') + 10);
            if ((h >= 'A') && (h <= 'F'))
                return ((h - 'A') + 10);
            return -1;
        }

        public static string UrlDecode(string s)
        {
            int length = s.Length;
            UrlDecoder decoder = new UrlDecoder(length, Encoding.UTF8);
            for (int i = 0; i < length; i++) {
                char ch = s [i];
                if (ch == '+')
                    ch = ' ';
                else
                if ((ch == '%') && (i < (length - 2))) {
                    if ((s [i + 1] == 'u') && (i < (length - 5))) {
                        int num3 = HexToInt(s [i + 2]);
                        int num4 = HexToInt(s [i + 3]);
                        int num5 = HexToInt(s [i + 4]);
                        int num6 = HexToInt(s [i + 5]);
                        if (((num3 < 0) || (num4 < 0)) || ((num5 < 0) || (num6 < 0)))
                            goto Label_0106;
                        ch = (char)((((num3 << 12) | (num4 << 8)) | (num5 << 4)) | num6);
                        i += 5;
                        decoder.AddChar(ch);
                        continue;
                    }
                    int num7 = HexToInt(s [i + 1]);
                    int num8 = HexToInt(s [i + 2]);
                    if ((num7 >= 0) && (num8 >= 0)) {
                        byte b = (byte)((num7 << 4) | num8);
                        i += 2;
                        decoder.AddByte(b);
                        continue;
                    }
                }
Label_0106:
                if ((ch & 0xff80) == 0)
                    decoder.AddByte((byte)ch);
                else
                    decoder.AddChar(ch);
            }
            return decoder.GetString();
        }
    }
}
