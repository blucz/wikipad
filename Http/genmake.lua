project "Http"
    category "_Libraries"
    kind     "SharedLib"
    language "C#"

    linksystemlibs {
        "System",
    }

    compilefiles {
        "http_server.cs",
        "http_headers.cs",
        "http_api.cs",
        "http_utils.cs",
        "http_mimetypes.cs",
        "http_unsafestring.cs",
        "http_datadictionary.cs",
        "http_multipart.cs",
        "http_cookie.cs",
    }

done "Http"

