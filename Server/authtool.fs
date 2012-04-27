module Wiki.AuthTool

open Wiki.Utils

[<EntryPoint>]
let main args =
    let args = Seq.toList args

    match args with 
        | user::password::[] -> 
            let hash  = sha1_str (user+":"+password)
            printfn "%s %s" user hash
            0
        | _                  ->
            eprintfn "usage: authtool <username> <password>"
            1
