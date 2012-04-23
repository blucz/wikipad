
solution "Wiki"
    dofile('genmake.os.lua')

    flags { "ExtraWarnings" }

    if (platform.is("windows")) then
        defines { "_CRT_SECURE_NO_WARNINGS", "_CRT_SECURE_NO_DEPRECATE", "WIN32" }
    end

    configuration "debug"
        defines "DEBUG"
        flags "Symbols"
        targetdir "bin/debug"
        objectsdir "obj/debug"
    done "debug"

    configuration "release"
        defines "NDEBUG"
        flags "Optimize"
        targetdir "bin/release"
        objectsdir "obj/release"
    done "release"
        
    include "Server"
    include "Http"
    include "LevelDb"

done "Wiki"

