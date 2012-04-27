module Wiki.Utils

open System
open System.IO
open System.Text
open HttpServer
open System.Collections.Generic
open System.Security.Cryptography

// utils to make functional style easier
let mkpath (a:string) (b:string) : string = Path.Combine(a,b)
let to_s (o:Object) : string = o.ToString()

let cleanpath (root:string) (path:string) = 
    let path = if path.StartsWith root then (path.Substring root.Length) else path
    path.TrimEnd [| '/' |]

let sendfile (tx:HttpTransaction) (path:string) =
    let fi = FileInfo path
    if fi.Exists then
        try
            tx.Response.Headers.SetNormalizedHeader ("Content-Type",  MimeTypes.GetMimeType path)
            let cachehit = match tx.Request.Headers.TryGetValue "If-Modified-Since"  with
                               | true,headerval -> fi.LastWriteTimeUtc <= (DateTime.Parse headerval)
                               | _              -> false 
            if cachehit then
                tx.Response.Respond HttpStatusCode.NotModified 
            else
                tx.Response.Headers.SetHeader           ("Last-Modified", fi.LastWriteTimeUtc.ToString ())
                tx.Response.Respond (HttpStatusCode.OK, File.ReadAllBytes path)
        with    
            | :? FileNotFoundException -> tx.Response.Respond(HttpStatusCode.NotFound);
            | e                        ->
                eprintfn "[error] %s" (to_s e)
                tx.Response.Respond HttpStatusCode.InternalServerError


let urlencode (s:string) =
    let is_safe (ch:char) =
        match ch with
            | ch when (ch >= 'a') && (ch <= 'z') -> true
            | ch when (ch >= 'A') && (ch <= 'Z') -> true
            | ch when (ch >= '0') && (ch <= '9') -> true
            | '(' -> true | ')' -> true | '*' -> true | '-' -> true
            | '.' -> true | '_' -> true | '!' -> true |  _  -> false
    let to_hex (n:byte) =
        let n = int n
        if n <= 9 then char (n + 0x30) else char ((n - 10) + 0x61)
    let sb = StringBuilder ()
    for b in Encoding.UTF8.GetBytes s do
        let c = char b
        let append (a:char) = ignore (sb.Append a)
        match c with 
            | c when is_safe c ->  c  |> append
            | ' '              -> '+' |> append
            | _                -> '%'                      |> append
                                  ((int b) >>> 4) &&& 0x0f |> byte |> to_hex |> append
                                  (int b) &&& 0x0f         |> byte |> to_hex |> append
    sb.ToString ()

let urldecode (s:string) =
    let idx = ref 0
    let bs : byte array = Array.zeroCreate s.Length
    let parse_hex (c:char) : int =
        match Char.ToLowerInvariant c with
            | '0' -> 0x0 | '1' -> 0x1 | '2' -> 0x2 | '3' -> 0x3
            | '4' -> 0x4 | '5' -> 0x5 | '6' -> 0x6 | '7' -> 0x7
            | '8' -> 0x8 | '9' -> 0x9 | 'a' -> 0xa | 'b' -> 0xb
            | 'c' -> 0xc | 'd' -> 0xd | 'e' -> 0xe | 'f' -> 0xf
            | _ -> failwith "invalid hex char"
    let rec proc (s:string) (n:int) =
        if n = s.Length then 
            ()
        else 
            match s.[n] with
                | '+'       -> bs.[!idx] <- byte ' '; idx := !idx + 1
                | '%'       -> 
                    let hi = int s.[!idx + 1]
                    let lo = int s.[!idx + 2]
                    bs.[!idx] <- byte ((hi <<< 4) ||| lo)
                    idx := !idx + 3
                | c         -> bs.[!idx] <- byte  c ; idx := !idx + 1
            proc s (n + 1)
    proc s 0
    Encoding.UTF8.GetString(bs, 0, !idx)

let redirect (tx:HttpTransaction) (path:string) =
    tx.Response.Headers.SetHeader ("Location", path)
    tx.Response.Respond HttpStatusCode.Found

let get_key (x:KeyValuePair<'k,'v>) : 'k = x.Key
let get_val (x:KeyValuePair<'k,'v>) : 'v = x.Value

let to_title_case (s:string) = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase s
let to_lower (s:string) = s.ToLowerInvariant()

let clamp x y z = max x y |> min z
let trim (s:string) = s.Trim ()
let trimstart (c:char) (s:string) = s.TrimStart [| c |]
let trimend (c:char) (s:string) = s.TrimEnd   [| c |]
let startswith (needle:string) (haystack:string) = haystack.StartsWith needle
let split (splitstr:string) (str:string) : string list = str.Split ([| splitstr |], StringSplitOptions.None) |> Array.toList
let wssplit (str:string) : string list = str.Split ([| ' ';'\t';'\n';'\r' |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
let skipchars (n:int) (s:string) = s.Substring n
let read_to_ws (str:string) (i:int) =
    match str |> Seq.skip i |> Seq.tryFindIndex Char.IsWhiteSpace with
        | Some idx -> i + idx, str.Substring (i, idx)
        | None     -> str.Length, str.Substring i

let xml_escape_append (sb:StringBuilder) (c:char) =
    match c with
        | '<'                         -> sb.Append "&lt;"   |> ignore
        | '>'                         -> sb.Append "&gt;"   |> ignore
        | '''                         -> sb.Append "&apos;" |> ignore
        | '"'                         -> sb.Append "&quot;" |> ignore
        | '&'                         -> sb.Append "&amp;"  |> ignore
        | c when Char.IsPunctuation c -> sb.Append c        |> ignore
        | c when Char.IsLetter      c -> sb.Append c        |> ignore
            | c when Char.IsDigit       c -> sb.Append c        |> ignore
        | c when Char.IsWhiteSpace  c -> sb.Append c        |> ignore
        | _                           -> int c |> sprintf "&#%d;" |> sb.Append |> ignore

let xml_escape (s:string) =
    let sb = StringBuilder()
    Seq.iter (xml_escape_append sb) s
    sb.ToString ()

let (|Prefix|_|) (i:int) (prefix:string) (str:string) =
    if str.IndexOf (prefix, i) = i then Some (i + prefix.Length) else None

let (|Wrap|_|) (i:int) (prefix:string) (suffix:string) (str:string) =
    match str with
        | Prefix i prefix new_i -> 
            match str.IndexOf (suffix, new_i) with
                | -1  -> None
                | idx -> Some (idx + suffix.Length, str.Substring (new_i,idx - new_i))
        | _                    -> None

let (|Int|_|) (str: string) =
    match Int32.TryParse str with
        | true, d -> Some d
        | _       -> None

let (|AnyChar|) (i:int) (str:string) = ((i+1), str.[i])

let xml_decode (s:string) =
    let sb = StringBuilder()
    let append (c:char) = sb.Append c |> ignore
    let rec loop i =
        if i = s.Length then ()
        else match s with
                 | Prefix i "&lt;"   (next_i)        -> append '<'      ; loop next_i 
                 | Prefix i "&gt;"   (next_i)        -> append '>'      ; loop next_i
                 | Prefix i "&apos;" (next_i)        -> append '''      ; loop next_i
                 | Prefix i "&quot;" (next_i)        -> append '"'      ; loop next_i
                 | Prefix i "&amp;"  (next_i)        -> append '&'      ; loop next_i
                 | Wrap   i "&#" ";" (next_i,Int c)  -> append (char c) ; loop next_i
                 | AnyChar i         (next_i, c)     -> append c        ; loop next_i
    loop 0
    sb.ToString ()
    
let (|StartsWith|_|) (prefix:string) (str:string) =
    if startswith prefix str then
        Some (skipchars prefix.Length str)
    else
        None

let is_empty (s:string) = s = ""

let to_base16 (bytes:byte[]) =
    let sb = StringBuilder ()
    let xx = "0123456789abcdef"
    for b in bytes do 
        let b = int b
        let highorder = (b >>> 4) &&& 0x0f
        let loworder  = b &&& 0x0f
        xx.[highorder] |> sb.Append |> ignore
        xx.[loworder]  |> sb.Append |> ignore
    sb.ToString ()

let sha1_str (s:string) : string =
    let provider = new SHA1CryptoServiceProvider()
    let bytes    = Encoding.UTF8.GetBytes s
    let hash     = provider.ComputeHash bytes
    to_base16 hash

