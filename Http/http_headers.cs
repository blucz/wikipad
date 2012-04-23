//
// Copyright (C) 2010 Jackson Harper (jackson@manosdemono.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
//


using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace HttpServer {

    public class HttpHeaders {

        public static readonly string CONTENT_LENGTH_KEY = "Content-Length";

        private long? content_length;
        private Encoding encoding;

        Dictionary<string,string> items = new Dictionary<string,string> ();

        public HttpHeaders ()
        {
        }

        public long? ContentLength {
            get { return content_length; }
            set {
                if (value < 0)
                    throw new ArgumentException ("value");

                content_length = value;
                if (value == null) {
                    items.Remove (CONTENT_LENGTH_KEY);
                    return;
                }
                items [CONTENT_LENGTH_KEY] = value.ToString ();
            }
        }

        public Encoding ContentEncoding {
            get {
                if (encoding == null)
                    SetEncodingInternal ();
                return encoding;
            }
            set {
                encoding = value;
            }
        }

        private void SetEncodingInternal ()
        {
            string content;

            if (!TryGetNormalizedValue ("Content-Type", out content)) {
                encoding = Encoding.UTF8;
                return;
            }

            string charset = HttpHeaders.GetAttribute (content, "; charset=");
            if (charset == null) {
                encoding = Encoding.Default;
                return;
            }

            try {
                encoding = Encoding.GetEncoding (charset);
            } catch (Exception e) {
                Console.Error.WriteLine ("[non-fatal] Exception while setting encoding:");
                Console.Error.WriteLine (e);

                encoding = Encoding.Default;
            }
        }

        public Dictionary<string,string>.KeyCollection Keys {
            get { return items.Keys; }
        }

        public string this [string name] {
            get {
                if (name == null)
                    throw new ArgumentNullException ("name");
                return items [NormalizeName (name)];
            }
        }

        public int Count {
            get { return items.Count; }	
        }

        public bool TryGetValue (string key, out string value)
        {
            return TryGetNormalizedValue (NormalizeName (key), out value);
        }

        public bool TryGetNormalizedValue (string key, out string value)
        {
            return items.TryGetValue (key, out value);
        }

        public void Parse (TextReader reader)
        {
            string line = reader.ReadLine ();
            while (line != null) {
                int line_end = line.Length - 1;

                if (String.IsNullOrEmpty (line))
                    return;

                if (Char.IsWhiteSpace (line [0]))
                    throw new HttpException ("Malformed HTTP header. Found whitespace before data.");

                while (Char.IsWhiteSpace (line [line_end])) {
                    line_end--;
                    if (line_end == 0)
                        throw new HttpException ("Malformed HTTP header. No data found.");
                }

                int colon = line.IndexOf (':');
                if (colon <= 0) 
                    throw new HttpException ("Malformed HTTP header. No colon found.");
                if (colon >= line_end)
                    throw new HttpException ("Malformed HTTP header. No value found.");
                string value = line.Substring (colon + 1, line_end - colon).TrimStart ();
                if (value.Length == 0)
                    throw new HttpException ("Malformed HTTP header. No Value found.");

                string key = line.Substring (0, colon);

                //
                // If the next line starts with whitespace its part of the current value
                //
                line = reader.ReadLine ();
                while (line != null && line.Length > 0 && Char.IsWhiteSpace (line [0])) {
                    value += " " + line.Trim ();
                    line = reader.ReadLine ();
                }

                SetHeader (key, value);
            }
        }

        public void Write (StringBuilder builder, ICollection<HttpCookie> cookies)
        {
            foreach (var h in items.Keys) {
                string header = (string) h;
                builder.Append (header);
                builder.Append (": ");
                builder.Append (items [header]);
                builder.Append ("\r\n");
            }

            if (cookies != null) {
                foreach (HttpCookie cookie in cookies) {
                    builder.Append (cookie.ToHeaderString ());
                }
            }
            builder.Append ("\r\n");
        }

        public void SetHeader (string name, string value)
        {
            if (name == null)
                throw new ArgumentNullException ("name");

            name = NormalizeName (name);

            SetNormalizedHeader (name, value);
        }

        public void SetNormalizedHeader (string name, string value)
        {
            if (!IsValidHeaderName (name))
                throw new ArgumentException (String.Format ("Invalid header '{0}'.", name));

            if (name == CONTENT_LENGTH_KEY) {
                SetContentLength (value);
                return;
            }

            if (value == null) {
                items.Remove (name);
                return;
            }

            //Console.WriteLine("set header '" + name + "' = '" + value + "'");
            items [name] = value;
        }

        public static string NormalizeName (string name)
        {
            if (String.IsNullOrEmpty (name))
                throw new ArgumentException ("name", "name must be a non-null non-empty string");

            StringBuilder res = null;

            if (Char.IsLower (name [0])) {
                res = new StringBuilder (name);
                res [0] = Char.ToUpper (name [0], CultureInfo.InvariantCulture);
            }

            char p = name [0];
            for (int i = 1; i < name.Length; i++) {
                char c = name [i];
                if (p == '-' && Char.IsLower (c)) {
                    if (res == null)
                        res = new StringBuilder (name);
                    res [i] = Char.ToUpper (c, CultureInfo.InvariantCulture);
                } else if (p != '-' && Char.IsUpper (c)) {
                    if (res == null)
                        res = new StringBuilder (name);
                    res [i] = Char.ToLower (c, CultureInfo.InvariantCulture);
                }
                p = c;
            }

            if (res != null)
                return res.ToString ();

            return name;
        }

        public bool IsValidHeaderName (string name)
        {
            // TODO: What more can I do here?
            if (name.Length == 0)
                return false;
            return true;
        }

        public void SetContentLength (string value)
        {
            if (value == null) {
                items.Remove (CONTENT_LENGTH_KEY);
                content_length = null;
                return;
            }

            int cl;
            if (!Int32.TryParse (value, out cl))
                throw new ArgumentException ("Malformed HTTP Header, invalid Content-Length value.", "value");
            if (cl < 0)
                throw new ArgumentException ("Content-Length must be a positive integer.", "value");
            ContentLength = cl;
        }

        /// from mono's System.Web/HttpRequest.cs
        public static string GetAttribute (string header_value, string attr)
        {
            int start = header_value.IndexOf (attr);
            if (start == -1)
                return null;

            start += attr.Length;
            if (start >= header_value.Length)
                return null;

            char ending = header_value [start];
            if (ending != '"')
                ending = ' ';

            int end = header_value.IndexOf (ending, start + 1);
            if (end == -1) {
                // Use the full string unless its a unclosed quote
                // TODO: What about multiline values, can they be broken across lines?
                return (ending == '"') ? null : header_value.Substring (start);
            }

            return header_value.Substring (start + 1, end - start - 1);
        }

        /// from mono's System.Web.Util/HttpEncoder.cs
        private static string EncodeHeaderString (string input)
        {
            StringBuilder sb = null;
            char ch;

            for (int i = 0; i < input.Length; i++) {
                ch = input [i];

                if ((ch < 32 && ch != 9) || ch == 127)
                    StringBuilderAppend (String.Format ("%{0:x2}", (int)ch), ref sb);
            }

            if (sb != null)
                return sb.ToString ();

            return input;
        }

        private static void StringBuilderAppend (string s, ref StringBuilder sb)
        {
            if (sb == null)
                sb = new StringBuilder (s);
            else
                sb.Append (s);
        }

        internal bool IsChunkedEncoding { get {
            string encoding;
            return TryGetNormalizedValue("Transfer-Encoding", out encoding) && encoding.Contains("chunked");
        } }

        internal void SetChunkedEncoding() {
            SetHeader("Transfer-Encoding", "chunked");
        }

        string /*?*/_GetNormalizedValue(string key) {
            string value;
            if (TryGetNormalizedValue(key, out value))
                return value;
            return null;
        }


        public HttpRange Range {
            get {
                string s;
                if (!TryGetNormalizedValue("Range", out s)) return null;
                return new HttpRange(s);
            } set {
                SetHeader("Range", value.ToString());
            }
        }

        public HttpContentRange ContentRange {
            get {
                string s;
                if (!TryGetNormalizedValue("Content-Range", out s)) return null;
                return new HttpContentRange(s);
            } set {
                SetHeader("Content-Range", value.ToString());
            }
        }

        public string UserAgent {
            get { return _GetNormalizedValue("User-Agent"); }
            set { SetHeader("User-Agent", value); }
        }

        public string Host {
            get { return _GetNormalizedValue("Host"); }
            set { SetHeader("Host", value); }
        }

        public bool IsConnectionClose {
            get { return _GetNormalizedValue("Connection") == "close"; }
            set { SetHeader("Connection", value ? "close" : null); }
        }

        public string ContentType {
            get { return _GetNormalizedValue("Content-Type"); }
            set { SetHeader("Content-Type", value); }
        }
    }
}

