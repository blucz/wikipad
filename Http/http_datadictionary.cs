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
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace HttpServer {
	/// <summary>
	/// A Heirarcical dictionary. Data can live at the "root" level, or in child dictionaries.
	/// DataDictionaries can store UnsafeStrings, lists of UnsafeStrings and dictionarys with
	/// strings as their key and UnsafeStrings as their values.  To add a list to the HttpDataDictionary
	/// simply add an item with a [] at the end of the keyname.  To add a dictionary you
	/// just add an item in this format keyname[key].
	/// </summary>
	public class HttpDataDictionary
	{
		private Dictionary<string,object> dictionary;
		private List<HttpDataDictionary> children;
		
		public HttpDataDictionary ()
		{
			dictionary = new Dictionary<string, object> ();
		}
		
		/// <summary>
		/// Get or set the string value at the specified key.
		/// </summary>
		/// <param name="key">
		/// A <see cref="System.String"/>
		/// </param>
		public string this [string key] {
			get { return GetString (key); }
			set { Set (key, value); }
		}

		public ICollection<string> Keys {
		       get { return dictionary.Keys; }
		}
		
		/// <summary>
		/// The sum of child dictionaries count and the count
		/// of the keys in this dictionary.
		/// </summary>
		public int Count {
			get {
				int sum = 0;
				if (children != null)
					children.Sum (c => c.Count);
				return sum + dictionary.Count;
			}
		}
		
		/// <summary>
		/// The child dictionaries.
		/// </summary>
		public IList<HttpDataDictionary> Children {
			get {
				if (children == null)
					children = new List<HttpDataDictionary>();
				return children;
			}
		}
		
		/// <summary>
		/// The "unsafe" string version of the value that is stored in this dictionary, or "null" if no value is stored for the specified key.
		/// </summary>
		/// <param name="key">
		/// A <see cref="System.String"/>
		/// </param>
		/// <returns>
		/// A <see cref="UnsafeString"/>
		/// </returns>
		public UnsafeString Get (string key)
		{
			return Get<UnsafeString> (key);
		}

		/// <summary>
		/// A list of unsafe strings stored with the specified
		/// key.  Lists are created when an item is added to
		/// the dictionary with [] at the end of its name.
		/// </summary>  
		public IList<UnsafeString> GetList (string key)
		{
			return Get<IList<UnsafeString>> (key);
		}

		/// <summary>
		/// A dictionary of Unsafe strings.  Dictionaries are
		/// created when an item is added to the
		/// HttpDataDictionary with a key in the format:
		/// foo[key].
		/// </summary>
		public IDictionary<string,UnsafeString> GetDict (string key)
		{
			return Get<IDictionary<string,UnsafeString>> (key);
		}

		private T Get<T> (string key)
		{
			object value = null;
			T t = default (T);
			
			if (dictionary.TryGetValue (key, out value)) {
				if (value is T)
					return (T) value;
			}
			
			if (children != null)
				children.Where (d => (t = d.Get<T> (key)) != null).FirstOrDefault ();
			
			return t;
		}
		
		/// <summary>
		/// Get a "safe" string from the dictionary, or, if the key doesn't exist in the dictionary, null.
		/// </summary>
		/// <param name="key">
		/// </param>
		/// <returns>
		/// The "safe" version of the value that is stored in the dictionary.
		/// </returns>
		public string GetString (string key)
		{
			UnsafeString str = Get (key);
			
			if (str == null)
				return null;
			
			return str.SafeValue;
		}
	
		/// <summary>
		/// Remove all elements from this dictionary, and remove all references to child dictionaries.
		/// </summary>
		public void Clear ()
		{
			dictionary.Clear ();
			children = null;
		}	
		
		/// <summary>
		/// Assign a value into this dictionary with the specified key.
		/// </summary>
		/// <param name="key">
		/// A <see cref="System.String"/>
		/// </param>
		/// <param name="value">
		/// A <see cref="System.String"/>
		/// </param>
		public void Set (string key, string value)
		{
			Set (key, new UnsafeString (value));
		}
		
		/// <summary>
		/// Assign a value into this dictionary with the specified key.
		/// </summary>
		/// <param name="key">
		/// A <see cref="System.String"/>
		/// </param>
		/// <param name="value">
		/// A <see cref="UnsafeString"/>
		/// </param>
		public void Set (string key, UnsafeString value)
		{
			int open = key.IndexOf ('[');
			if (open == -1) {
				dictionary [key] = value;
				return;
			}

			string elkey = key.Substring (0, open);
			int close = key.IndexOf (']');
			if (close == -1 || close < open) {
				dictionary [elkey] = value;
				return;
			}

			object col;
			if (close == open + 1) {
				List<UnsafeString> list = null;

				if (dictionary.TryGetValue (elkey, out col)) {
					list = col as List<UnsafeString>;
					if (list != null) {
						list.Add (value);
						return;
					}
				}

				list = new List<UnsafeString> ();
				list.Add (value);
				dictionary [elkey] = list;
				
				return;
			}

			Dictionary<string,UnsafeString> dict = null;
			string dname = UnsafeString.Escape (key.Substring (open + 1, close - open - 1));
			if (dictionary.TryGetValue (elkey, out col)) {
				dict = col as Dictionary<string,UnsafeString>;
				if (dict != null) {
					dict [dname] = value;
					return;
				}
			}

			dict = new Dictionary<string,UnsafeString> ();
			dict [dname] = value;
			dictionary [elkey] = dict;
		}
	}
}

