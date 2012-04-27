project "Server"
    category "Test"
    kind     "ConsoleApp"
    language "F#"

    linksystemlibs {
        "System",
        "System.Core",
        "System.Runtime.Serialization",
    }

    linkprojects {
        "Http",
        "LevelDb",
    }

    compilefiles {
        "utils.fs",
        "wiki.fs",
        "main.fs",
    }

done "Server"

project "authtool"
    category "Test"
    kind     "ConsoleApp"
    language "F#"

    linksystemlibs {
        "System",
        "System.Core",
        "System.Runtime.Serialization",
    }

    linkprojects {
        "Http",
        "LevelDb",
    }

    compilefiles {
        "utils.fs",
        "authtool.fs",
    }

done "authtool"
