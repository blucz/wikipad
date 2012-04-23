project "LevelDb"
    category    "_Libraries"
    kind        "SharedLib"
    language    "C#"
    flags       "Unsafe"

    linksystemlibs {
        "System",
        "System.Xml",
    }

    compilefiles {
        "leveldb.cs",
        "leveldb_native.cs",
    }

    copybinaries_sharedlib "leveldb"

done "LevelDb"


