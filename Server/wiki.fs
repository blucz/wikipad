module Wiki.Parser
open System
open System.Text

open Wiki.Utils

type private InlineFormat = Strong | Em

(* transforms wikicreole 1.0 into html *)
let wiki_creole (input:string) (getlink : string -> string) (getimage : string -> string) : string =
    let lines     = split "\n" input
    let sb        = StringBuilder ()

    (* active patterns to help with char-level parsing *)
    let (|PipeSplit|_|) (str:string) =
        match str.IndexOf "|" with -1  -> None | idx -> Some (str.Substring(0,idx),str.Substring(idx+1))
    let (|Url|_|)    (i:int) (str:string) = 
        match str with 
            | Prefix i  "http://" _ -> Some (read_to_ws str i)     
            | Prefix i  "ftp://"  _ -> Some (read_to_ws str i)     
            | _                     -> None
    let (|EscUrl|_|) (i:int) (str:string) = 
        match str with 
             | Prefix i "~http://" _ -> Some (read_to_ws str (i+1)) 
             | Prefix i "~ftp://"  _ -> Some (read_to_ws str (i+1)) 
             | _                     -> None
    let (|Esc|_|) (i:int) (str:string) = 
        if str.Length > (i+1) && str.[i] = '~' then Some (i+1) else None

    (* utility functions *)
    let opentag  (s:string) = sprintf "<%s>"  s
    let closetag (s:string) = sprintf "</%s>" s
    let append (s:string) = sb.Append s |> ignore
    let append_line (s:string) = sb.AppendLine s |> ignore
    let append_escaped_char (c:char) = xml_escape_append sb c
    let append_escaped (s:string) = Seq.iter append_escaped_char s
    let append_escaped_line (s:string) = append_escaped s; append_line ""
    let append_content (content:string) =
        let content = trim content
        let rec parse i fmtstack nowiki =
            if i = content.Length then fmtstack,nowiki
            else match fmtstack, nowiki, content with
                     | Strong::rest, false, Prefix i "**"   new_i      -> append "</strong>" ; parse new_i rest nowiki
                     | _           , false, Prefix i "**"   new_i      -> append "<strong>"  ; parse new_i (Strong::fmtstack) nowiki
                     | Em::rest    , false, Prefix i "//"   new_i      -> append "</em>"     ; parse new_i rest nowiki
                     | _           , false, Prefix i "//"   new_i      -> append "<em>"      ; parse new_i (Em::fmtstack) nowiki
                     | _           , false, Prefix i "\\\\" new_i      -> append "<br />"    ; parse new_i fmtstack nowiki
                     | _           , false, Prefix i "{{{"  new_i      -> append "<code>"    ; parse new_i fmtstack true
                     | _           , true , Prefix i "}}}"  new_i      -> append "</code>"   ; parse new_i fmtstack false
                     | _           , false, EscUrl i (new_i, url)      -> append_escaped url; parse new_i fmtstack nowiki
                     | _           , false, Esc i       new_i          -> 
                         if new_i < content.Length then append_escaped_char content.[new_i]; parse (new_i+1) fmtstack nowiki
                                                   else parse new_i fmtstack nowiki
                     | _           , false, Url i (new_i, url)         -> 
                        sprintf "<a href='%s'>" url |> append; append_escaped url; append "</a>"
                        parse new_i fmtstack nowiki
                     | _           , false, Wrap i "{{" "}}" (new_i, PipeSplit (id,text)) -> 
                        sprintf "<img src='%s' alt='%s' />" (getimage id) text |> append
                        parse new_i fmtstack nowiki
                     | _           , false, Wrap i "[[" "]]" (new_i, PipeSplit (id,text)) -> 
                        sprintf "<a href='%s'>" (getlink id) |> append; append_escaped text; append "</a>"
                        parse new_i fmtstack nowiki
                     | _           , false, Wrap i "{{" "}}" (new_i, id) -> 
                        sprintf "<img src='%s' />" (getimage id) |> append
                        parse new_i fmtstack nowiki
                     | _           , false, Wrap i "[[" "]]" (new_i, id) -> 
                        sprintf "<a href='%s'>%s</a>" (getlink id) id |> append
                        parse new_i fmtstack nowiki
                     | _                                                      -> append_escaped_char content.[i]; parse (i+1) fmtstack nowiki
        let fmtstack,nowiki = parse 0 [] false
        if nowiki then
            append "</code>"
        for fmt in fmtstack do 
            match fmt with
                | Em     -> append "</em>"
                | Strong -> append "</strong>"
    let append_content_line (content:string) = append_content content; append_line ""
    let append_tag tag content =
        opentag tag  |> append
        content      |> append_content
        closetag tag |> append

    (* perf: remove intermediate string copies for trim *)
    let is_list_start    (listchar:char) (line:string) = let line = trim line in line.Length >= 2 && line.[0] = listchar && line.[1] <> listchar
    let is_nowiki_start  (line:string)                 = let line = trim line in line = "{{{"
    let is_nowiki_end    (line:string)                 = let line = trim line in line = "}}}"
    let is_hr_start      (line:string)                 = let line = trim line in line = "----"
    let is_empty         (line:string)                 = let line = trim line in line = ""
    let is_header_start  (line:string)                 = let line = trim line in line.Length > 0 && line.[0] = '='
    let is_table_start   (line:String)                 = let line = trim line in line.Length > 0 && line.[0] = '|'
    let is_theader_start (line:String)                 = let line = trim line in line.Length > 1 && line.[0] = '|' && line.[1] = '='

    let opentag  (s:string) = sprintf "<%s>"  s
    let closetag (s:string) = sprintf "</%s>" s

    (* line-level processing *)
    let proc_table lines = 
        let rec proc_headers lines = 
            match lines with 
                | line::rest when is_theader_start line -> 
                    let line   = line |> trim |> trimend '|' |> skipchars 2 
                    let splits = split "|=" line |> List.map trim
                    splits |> Seq.iter (append_tag "th")
                    proc_headers rest
                | lines -> lines

        let rec proc_data lines =
            match lines with
                | line::rest when is_table_start line ->
                    let line   = line |> trim |> trimend '|' |> skipchars 1
                    let splits = split "|" line |> List.map trim
                    append "<tr>"
                    splits |> Seq.iter (append_tag "td")
                    append_line "</tr>"
                    proc_data rest
                | lines -> lines

        append_line "<table>"
        let firstline = List.head lines
        let rest = if is_theader_start firstline then
                       append "<tr>"
                       let rest = proc_headers lines
                       append_line "</tr>"
                       rest
                   else lines

        let rest = rest |> proc_data
        append_line "</table>"
        rest

    let proc_header lines =
        let hline         = List.head lines |> trim |> trimend '='
        let content       = hline |> trimstart '='
        let hlevel        = clamp 1 (hline.Length - content.Length) 6
        let tag = sprintf "h%d" hlevel
        append_tag tag content
        append_line ""
        List.tail lines

    let rec proc_list lines depth listchar tag =
        append_line (opentag tag)
        let is_list_end (line:string) = 
            match line with
                | line when is_empty          line -> true
                | line when is_nowiki_start   line -> true
                | line when is_hr_start       line -> true
                | line when is_header_start   line -> true
                | line when is_table_start    line -> true
                | _                                -> false

        let rec proc_list_line isopen lines =
            match lines with 
                | line::rest when is_list_end line -> if isopen then append_line "</li>"
                                                      lines                                              // end of list
                | line::rest ->
                    let line = trim line
                    let content  = line |> trimstart listchar 
                    let newdepth = line.Length - content.Length
                    match newdepth with
                        | 0                           -> append_content_line content
                                                         proc_list_line isopen rest
                        | _ when newdepth = depth     -> if isopen then append_line "</li>"
                                                         append_line "<li>"                                 // item at this level
                                                         append_content_line content 
                                                         proc_list_line true rest
                        | _ when newdepth = depth + 1 -> let rest = proc_list lines newdepth listchar tag   // go deeper
                                                         append_line "</li>"    
                                                         proc_list_line false rest
                        | _                           -> if isopen then append_line "</li>"
                                                         lines                                              // end of list
                | []         -> []
        let rest = proc_list_line false lines 
        append_line (closetag tag)
        rest

    let proc_para lines =
        append_line "<p>"
        let parsb = StringBuilder ()
        let rec proc_parlines lines =
            match lines with 
                | line::rest when is_empty          line -> lines
                | line::rest when is_nowiki_start   line -> lines
                | line::rest when is_hr_start       line -> lines
                | line::rest when is_list_start '#' line -> lines
                | line::rest when is_list_start '*' line -> lines
                | line::rest when is_header_start   line -> lines
                | line::rest when is_table_start    line -> lines
                | line::rest                             -> parsb.AppendLine (trim line) |> ignore; proc_parlines rest
                | []                                     -> []
        let rest = proc_parlines lines
        parsb.ToString () |> append_content 
        append_line "\n</p>"
        rest

    let rec proc_nowiki lines =
        match lines with
            | line::rest when is_nowiki_start line -> append"<pre><code>"   ; proc_nowiki rest
            | line::rest when is_nowiki_end line   -> append_line "</code></pre>" ; rest
            | line::rest                           -> append_escaped_line line      ; proc_nowiki rest 
            | []                                   -> append_line "</code></pre>" ; []

    let rec proc_lines (lines : string list) : unit =
        let rest = match lines with
                       | line::rest when is_empty          line -> rest                      
                       | line::rest when is_nowiki_start   line -> proc_nowiki lines   
                       | line::rest when is_hr_start       line -> append_line "<hr />"; List.tail lines
                       | line::rest when is_list_start '*' line -> proc_list   lines 1 '*' "ul"
                       | line::rest when is_list_start '#' line -> proc_list   lines 1 '#' "ol"
                       | line::rest when is_header_start   line -> proc_header lines   
                       | line::rest when is_table_start    line -> proc_table  lines   
                       | line::rest                             -> proc_para   lines
                       | []                                     -> []
        if rest <> [] then proc_lines rest

    proc_lines lines
    sb.ToString ()
