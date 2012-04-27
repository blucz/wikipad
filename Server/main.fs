module Wiki.Server

open System
open System.IO
open System.Text
open System.Threading
open System.Runtime.Serialization

open HttpServer

open Wiki.Utils
open Wiki.Parser

// argument processing
let mutable dbpath     = "db"           // path to databases
let mutable staticpath = "static"       // path to static resources
let mutable port       = 8000           // port for http server
let mutable root       = "/wiki"        // path of wiki relative to the domain
let mutable domain     = "http://blucz.com"
let mutable disqus     = None
let mutable authfile   = None

for arg in Environment.GetCommandLineArgs () do
    match arg with 
        | StartsWith "-static="   rest      -> staticpath <- rest
        | StartsWith "-db="       rest      -> dbpath     <- rest
        | StartsWith "-port="     (Int num) -> port       <- num
        | StartsWith "-root="     rest      -> root       <- rest
        | "-disqus="                        -> ()               // support empty arg 
        | StartsWith "-disqus="   rest      -> disqus     <- Some rest
        | "-auth="                          -> ()              
        | StartsWith "-auth="     rest      -> authfile   <- Some rest
        | _                                 -> eprintfn "Unrecognized argument: %s" arg

// environment
ignore (Directory.CreateDirectory dbpath)
let server      = new HttpServer   (port = port)
let meta_db     = new LevelDb.Database (mkpath dbpath "meta.db")
let content_db  = new LevelDb.Database (mkpath dbpath "content.db")
let session_db  = new LevelDb.Database (mkpath dbpath "session.db")
let openauth    = authfile = None

let auth (user:string) (password:string) : bool =
    let hash  = sha1_str (user+":"+password)
    match authfile with 
        | None          -> true
        | Some filename ->
            let rec proclines lines =
                match lines with
                    | line::rest ->
                        let splits      = wssplit line
                        let luser,lhash = splits.[0],splits.[1]
                        if luser = user && lhash = hash then true else proclines rest
                    | []         -> false
            File.ReadAllLines filename |> Seq.filter (is_empty >> not) |> Seq.toList |> proclines

type Version = {
    mutable modtime : DateTime
    mutable id      : Guid
}

type Page = {
    mutable key        : string
    mutable title      : string
    mutable modtime    : DateTime
    mutable versions   : Version array }
with
    member self.content_id : Guid =
        match self.versions.Length with 
            | 0     -> failwith "invalid op"
            | _     -> self.versions.[0].id

let page_serializer = DataContractSerializer (typeof<Page>, [| typeof<Version> |])

let normalize (pagekey:string) : string = pagekey |> trim |> to_lower

let deserialize_page (arr:byte[]) : Page =
    use ms = new MemoryStream (arr)
    (page_serializer.ReadObject ms) :?> Page

let serialize_page (page:Page) : byte[] =
    use ms = new MemoryStream ()
    page_serializer.WriteObject (ms, page)
    ms.ToArray ()

let save_content (id:Guid) (content:string) =
    let key  = id.ToByteArray()
    let data = Encoding.UTF8.GetBytes content
    content_db.Put (key,data)

let save_page (page:Page) =
    let key  = Encoding.UTF8.GetBytes (normalize page.key)
    let data = serialize_page page
    meta_db.Put (key,data)

let del_page (pagekey:string) =
    let key  = Encoding.UTF8.GetBytes pagekey
    meta_db.Delete key

let load_content (id:Guid) : string =
    let key = id.ToByteArray()
    match content_db.TryGetValue key with 
        | true,value -> Encoding.UTF8.GetString value
        | _          -> failwith "page not found"

let load_page (key:string) : Page option =
    let key = Encoding.UTF8.GetBytes (normalize key)
    match meta_db.TryGetValue key with 
        | true,value -> Some (deserialize_page value)
        | _          -> None

let normalize_encode (pagekey:string) =
    pagekey |> normalize |> urlencode

let static_url (file:string)    = sprintf "%s/static/%s" root file
let page_url (pagekey:string)   = sprintf "%s/%s" root (normalize_encode pagekey)
let edit_url (pagekey:string)   = sprintf "%s/%s/edit" root (normalize_encode pagekey)
let save_url (pagekey:string)   = sprintf "%s/%s/save" root (normalize_encode pagekey)
let del_url (pagekey:string)    = sprintf "%s/%s/del" root (normalize_encode pagekey)
let cookieme_url                = sprintf "%s/cookieme" root
let uncookieme_url              = sprintf "%s/uncookieme" root

let ev_401 (tx:HttpTransaction) =
    tx.Response.Respond HttpStatusCode.Unauthorized

let ev_404 (tx:HttpTransaction) =
    tx.Response.Respond HttpStatusCode.NotFound

let load_pages () =
    Seq.map (get_val >> deserialize_page) meta_db

let dummy_page (key:string) = 
    { Page.key = normalize key; title = to_title_case key; modtime = DateTime.Now; versions = [| |]; }

let render_left () : string =
    let render_link (page:Page) = 
        sprintf "<div class='leftlink'><a href='%s'>%s</a></div>" (page_url page.key) page.title
    let sortkey (p:Page) = 
        let key = p.key.ToLowerInvariant ()
        if key = "home" then "0000000000000" else key
    load_pages () |> Seq.sortBy sortkey |> Seq.map render_link |> String.concat "\n"

let render_page authenticated title title_link body = 
    let auth = if openauth then "" 
                           else if authenticated then sprintf @"<a class='uncookieme' href='%s'>&nbsp;</a>" uncookieme_url
                                                 else sprintf @"<a class='cookieme'   href='%s'>&nbsp;</a>" cookieme_url
    sprintf @"<html>
        <head>
            <title>%s</title>
            <link rel='stylesheet' href='%s' />
            <script src='%s'></script>
        </head>
        <body>
            <div id='container'>
            <div id='top'>
                <span class='title'>
                    <a href='%s'>%s</a>
                </span>
                %s
            </div>
            <div id='main'>
                <div id='left'>
                    %s
                </div>
                <div id='content'>
                    %s
                </div>
            </div>
        </body>
    </html>" title (static_url "style.css") (static_url "jquery-1.7.2.min.js") title_link title auth (render_left ()) body

let ev_confirm_del (tx:HttpTransaction) (authenticated:bool) (pagekey:string) =
    if authenticated then
        let page = match load_page pagekey with
                      | Some page -> page
                      | None      -> (dummy_page pagekey)
        let body = sprintf @"<div class='confirm'>
                                 Are you sure you want to delete %s?
                                 <div class='confirmbuttons' align='right'>
                                     <form action='%s' method='post' style='display:inline'>
                                         <input type='hidden' name='noconfirm' value='true' />
                                         <input type='submit' value='Cancel'></input>
                                     </form>
                                     <form action='%s' method='post' style='display:inline'>
                                         <input type='hidden' name='confirm' value='true' />
                                         <input type='submit' value='Delete'></input>
                                     </form>
                                 <div>
                             </div>" page.title (del_url pagekey) (del_url pagekey) 
        let page = render_page authenticated page.title (page_url page.key) body
        tx.Response.Respond (HttpStatusCode.OK, page)
    else
        ev_401 tx

let ev_del (tx:HttpTransaction) (authenticated:bool) (pagekey:string) =
    if authenticated then
        if (tx.Request.PostData.GetString "noconfirm") = "true" then
            redirect tx (page_url pagekey)
        else if (tx.Request.PostData.GetString "confirm") = "true" then
            del_page pagekey
            redirect tx (page_url "home")
        else
            ev_confirm_del tx authenticated pagekey
    else
        ev_401 tx

let ev_save (tx:HttpTransaction) (authenticated:bool) (pagekey:string) =
    if authenticated then
        let page,content = match load_page pagekey with
                               | Some page -> page,(load_content page.content_id)
                               | None      -> (dummy_page pagekey),""

        let newcontent = (tx.Request.PostData.GetString "content").Trim()
        let newtitle   = (tx.Request.PostData.GetString "title").Trim()

        let newcontent = xml_decode newcontent

        if page.title <> newtitle || content <> newcontent then
            let newmodtime  = DateTime.Now
            let contentid   = Guid.NewGuid ()
            let newversion  = { Version.modtime = newmodtime; id = contentid }
            let newversions = newversion::(Array.toList page.versions) |> List.toArray
            page.title    <- newtitle
            page.modtime  <- newmodtime
            page.versions <- newversions
            save_content contentid newcontent
            save_page page
        redirect tx (page_url pagekey)
    else
        ev_401 tx

let ev_edit (tx:HttpTransaction) (authenticated:bool) (pagekey:string) =
    if authenticated then
        let page,content = match load_page pagekey with
                               | Some page -> page,(load_content page.content_id)
                               | None      -> (dummy_page pagekey),""
        let editor = sprintf @"<form action='%s' method='post'>
                                   <div class='editor'>
                                       <div class='editorheading'>Title</div>
                                       <input type='text' name='title' value='%s' class='editortitle' />

                                       <div class='editorheading'>Content</div>
                                       <textarea name='content' class='editortext'>%s</textarea>

                                       <div class='submitcontainer'>
                                           <input type='hidden' name='pagekey' value='%s'></input>
                                           <input type='submit' value='Save'></input>
                                       </div>
                                   </div>
                               </form>" (save_url pagekey) page.title (xml_escape content) pagekey
        let page = render_page authenticated page.title (page_url page.key) editor
        tx.Response.Respond (HttpStatusCode.OK, page)
    else
        ev_401 tx

let render_dummy_content (pagekey:string) =
    sprintf @"<div class='content'>
                  <div class='contentwrapper'>
                    <p>
                      This page doesn't exist yet
                      </p>
                  </div>
                  <div class='editlinks' align='right'>
                      <div class='leftlink'>
                          <a href='%s'>Create</a> 
                      </div>
                  </div> 
              </div>" (edit_url pagekey)

let render_comments_switch (pagekey:string) =
    match disqus with 
        | None        -> ""
        | Some disqus -> sprintf @"<div id='comments_switch'>
                                       <a href='#disqus_thread' class='on' data-disqus-identifier='%s'>&nbsp;</a>
                                       <a href='#' class='off' style='display:none'>Hide Discussion</a>
                                   </div>" pagekey

let render_comments (pagekey:string) =
    match disqus with 
        | None        -> ""
        | Some disqus -> sprintf @"<div id='comments'>
                                       <div id='disqus_thread'></div>
                                       <script type='text/javascript'>
                                           $(function() {
                                               $('#comments_switch .on').click(function(e) {
                                                   $('#comments_switch .on').hide()
                                                   $('#comments_switch .off').show()
                                                   $('#comments').show()
                                                   e.preventDefault()
                                               });
                                               $('#comments_switch .off').click(function(e) {
                                                   $('#comments_switch .off').hide()
                                                   $('#comments_switch .on').show()
                                                   $('#comments').hide()
                                                   e.preventDefault()
                                               });
                                           });
 
                                           var disqus_shortname  = '%s';
                                           var disqus_identifier = '%s';
 
                                           (function() {
                                               var dsq = document.createElement('script'); dsq.type = 'text/javascript'; dsq.async = true;
                                               dsq.src = 'http://' + disqus_shortname + '.disqus.com/embed.js';
                                               (document.getElementsByTagName('head')[0] || document.getElementsByTagName('body')[0]).appendChild(dsq);
                                           })();
 
                                           (function() {
                                               var dsq = document.createElement('script'); dsq.type = 'text/javascript'; dsq.async = true;
                                               dsq.src = 'http://' + disqus_shortname + '.disqus.com/count.js';
                                               (document.getElementsByTagName('head')[0] || document.getElementsByTagName('body')[0]).appendChild(dsq);
                                           })();
                                       </script>
                                       <noscript>Please enable JavaScript to view the <a href='http://disqus.com/?ref_noscript'>comments powered by Disqus.</a></noscript>
                                       <a href='http://disqus.com' class='dsq-brlink'>Comments powered by <span class='logo-disqus'>Disqus</span></a>
                                   </div>" disqus pagekey

let ev_page (tx:HttpTransaction) (authenticated:bool) (pagekey:string) =
    let render_content (pagekey:string) (content:string) =
        let content         = wiki_creole content page_url id
        let comments        = render_comments        pagekey
        let comments_switch = render_comments_switch pagekey
        let editlinks       = if authenticated then sprintf @"<div class='editlinks' align='right'>
                                                                  <a href='%s'>Edit</a> | <a href='%s'>Delete</a>
                                                              </div>" (edit_url pagekey) (del_url pagekey) 
                                               else ""
        sprintf @"<div class='content'>
                      <div class='contentwrapper'>
                          %s
                      </div>
                      <div class='belowcontent'>
                          %s
                          %s
                          &nbsp;
                      </div>
                      %s
                  </div>" content editlinks comments_switch comments

    let page,content = match load_page pagekey with
                           | Some page -> page,(load_content page.content_id |> render_content pagekey)
                           | None      -> (dummy_page pagekey),(render_dummy_content pagekey)
    let rendered = render_page authenticated page.title (page_url page.key) content
    tx.Response.Respond (HttpStatusCode.OK, rendered)

let ev_uncookieme (tx:HttpTransaction) (authenticated:bool) =
    if authenticated then
        tx.Response.RemoveCookie "token"
    match tx.Request.Headers.TryGetValue("Referer") with 
        | false,_       -> redirect tx root
        | true ,referer -> redirect tx referer

let ev_cookieme (tx:HttpTransaction) (authenticated:bool) =
    match tx.Request.PostData.["user"],tx.Request.PostData.["password"],tx.Request.PostData.["redirect_to"] with
        | null,null,_    -> 
            let referer = match tx.Request.Headers.TryGetValue("Referer") with 
                              | false,_      -> ""
                              | true,referer -> sprintf "<input type='hidden' name='redirect_to' value='%s'>" referer
            let body = sprintf @"<div class='login'>
                                       <form action='%s' method='post'>
                                           <br />
                                           <input type='text' name='user' autofocus='true' /><br />
                                           <input type='password' name='password' /><br />
                                           %s
                                           <br />
                                           <input type='submit' value ='Log in' />
                                       </form>
                                   </div>" cookieme_url referer
            let page = render_page false "Log In" cookieme_url body
            tx.Response.Respond (HttpStatusCode.OK, page)

        | user,password,redirect_to ->
            if auth user password then
                let token = Guid.NewGuid().ToString("N")
                session_db.Put (Encoding.UTF8.GetBytes token, Encoding.UTF8.GetBytes user)
                tx.Response.SetCookie("token", token) |> ignore
                redirect tx (if redirect_to = null then root else redirect_to)
            else
                redirect tx cookieme_url

// toplevel request handler
let ev_request (tx:HttpTransaction) =
    try
        let path = cleanpath root tx.Request.Path
        printfn "REQUEST %s ==> %s" tx.Request.Path path

        let strip_suffix (p:string) (q:string) =
            p.Substring (0, p.Length - q.Length)

        let authenticated = match tx.Request.Cookies.GetString "token" with
                                | null  -> false || openauth
                                | token -> match session_db.TryGetValue (Encoding.UTF8.GetBytes token) with
                                               | true,_ -> true
                                               | _      -> false || openauth

        let handle_page_action (p:string) (cb:HttpTransaction -> bool -> string -> unit) =
            let page = p.TrimStart [| '/' |] |> urldecode |> to_lower
            if page.Contains "/" then ev_404 tx
                                 else cb tx authenticated (urldecode page)

        match path with
            | ""                                   -> page_url "home" |> redirect tx 
            | "/favicon.ico"                       -> mkpath staticpath "favicon.ico" |> sendfile tx
            | "/uncookieme"                        -> ev_uncookieme tx authenticated
            | "/cookieme"                          -> ev_cookieme   tx authenticated
            | p when p.StartsWith "/static/"       -> 
                let filename = p.Substring "/static/".Length
                if filename.Contains ".." then ev_404 tx
                else mkpath staticpath filename |> sendfile tx
            | p when p.EndsWith "/save"            -> handle_page_action (strip_suffix p "/save") ev_save
            | p when p.EndsWith "/del"             -> handle_page_action (strip_suffix p "/del")  ev_del 
            | p when p.EndsWith "/edit"            -> handle_page_action (strip_suffix p "/edit") ev_edit
            | p                                    -> handle_page_action p ev_page
    with e ->
        printfn "Error processing request: %s" (e.ToString ())
        tx.Response.Respond HttpStatusCode.InternalServerError
 
server.add_HandleRequest (HttpHandlerDelegate ev_request)
server.Start ()
printfn "Listening on port %d" server.BoundPort
Thread.Sleep Timeout.Infinite
