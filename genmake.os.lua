-----------------------------------------------------------------------------------------
-----------------------------------------------------------------------------------------
--
-- Platform/Arch toolchain configuration
-- 
-----------------------------------------------------------------------------------------
-----------------------------------------------------------------------------------------

compilers.Mono_CSC = "dmcs"
compilers.ScalaC = "scalac"

--print ("BUILDING FOR ARCHITECTURE       " .. arch.get())
--print ("BUILDING FOR PLATFORM           " .. platform.get())

if arch.is('arm') then
    defines { "ARCH_ARM" }
elseif arch.is('x86') then
    defines { "ARCH_X86" }
elseif arch.is('x64') then
    defines { "ARCH_X64" }
else
    error("unknown architecture");
end

-- 
-- paths are set up in our cygwin environment to point to SDK assets
--
-- in order to actually pass these paths into tools, we need to translate
-- these into windows paths.
--
-- This is non-trivial, since it requires resolving drive letters, following
-- cygwin symlinks, etc, so we let cygpath do the heavy lifting.
--
-- This routine is slow, so avoid calling it more than once for the same path
--
function translate_path_for_tool(p)
    if os.is('windows') then
        os.rmfile('.genmake_path_tmp')
        local ret = os.execute("cygpath -wa '" .. p .. "' > .genmake_path_tmp")
        if not (ret == 0) then 
            error("error executing cygpath -wa '" .. p .. "'")
        end
        local f = assert(io.open('.genmake_path_tmp', 'rt'))
        for line in f:lines() do
            f:close()
            os.rmfile('.genmake_path_tmp')
            --print ('translated path [' .. p .. '] ===> [' .. line .. ']')
            return line
        end
        os.rmfile('.genmake_path_tmp')
        error('failed to read line from cygpath')
    end
    return p
end

if platform.is('windows') then
    if arch.is('x86') then
        defines { "PLATFORM_WINDOWS" }
        if (os.isdir("c:\\Program Files (x86)\\Microsoft Visual Studio 10.0")) then
            VSROOT = "c:\\Program Files (x86)\\Microsoft Visual Studio 10.0";
            os.setenv("PATH", "c:\\Program Files (x86)\\Microsoft Visual Studio 10\\Common7\\IDE:" .. os.getenv("PATH"))
            includedirs { "c:\\Program Files (x86)\\Microsoft SDKs\\Windows\\v7.0A\\Include" }
            libdirs { "c:\\Program Files (x86)\\Microsoft SDKs\\Windows\\v7.0A\\lib" }
        elseif (os.isdir("c:\\Program Files\\Microsoft Visual Studio 10.0")) then
            VSROOT = "c:\\Program Files\\Microsoft Visual Studio 10.0";
            includedirs { "c:\\Program Files\\Microsoft SDKs\\Windows\\v7.0A\\Include" }
            libdirs { "c:\\Program Files\\Microsoft SDKs\\Windows\\v7.0A\\lib" }
        else
            error("you don't seem to have the microsoft platform sdk or visual studio installed.");
        end

        libdirs { VSROOT .. "\\VC\\lib" }
        includedirs { VSROOT .. "\\VC\\include" }

        compilers.MSNET_CSC = "c:\\WINDOWS\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe"
        compilers.MSNET_CSC_DEFINES = { "MSNET" }
        compilers.MSNET_RESGEN = "c:\\Program Files\\Microsoft SDKs\\Windows\\v6.0A\\bin\\resgen.exe"

        compilers.MSNET_FSC = "C:\\Program Files (x86)\\Microsoft F\\#\\v4.0\\Fsc.exe"
        compilers.MSNET_FSC_DEFINES = { "MSNET" }

        compilers.MS_CC = VSROOT .. "\\vc\\bin\\cl.exe"
        compilers.MS_CXX = VSROOT .. "\\vc\\bin\\cl.exe"
        compilers.MS_SHAREDLINK = VSROOT .. "\\vc\\bin\\link.exe"
        compilers.MS_STATICLINK = VSROOT .. "\\vc\\bin\\lib.exe"

        compilers.EDITBIN = VSROOT .. "\\VC\\bin\\editbin"

        compilers.MSNET_CSC_FLAGS = "-platform:x86"
        compilers.MSNET_FSC_FLAGS = "--platform:x86"
    else
        error('unsupported windows architecture')
    end
end

if platform.is('macosx') then
    defines { "PLATFORM_MACOSX", "PLATFORM_OSX" }
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
    compilers.Mono_CSC_FLAGS  = "-platform:x86"
    compilers.Mono_FSC_FLAGS  = "--platform:x86"
    compilers.Mono_FSC = "/Library/Frameworks/Mono.framework/Versions/Current/bin/fsc"
    compilers.Mono_FSC_DEFINES = { "MONO" }
    compilers.GCC_AR = 'libtool'
    compilers.GCC_AR_FLAGS = "-static -o"

    if arch.is('x86') then
        compilers.GCC_CC_FLAGS = '-m32 -mmacosx-version-min=10.5'
        compilers.GCC_CXX_FLAGS = '-m32 -mmacosx-version-min=10.5'
    else
        error('unsupported macosx architecture')
    end
end

if platform.is('webos') then
    defines { "PLATFORM_WEBOS" }
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
    
    local webos_sdk            = os.getenv('PALMSDK')
    local webos_pdk            = os.getenv('PALMPDK')
    local webos_toolchain_path = os.getenv('PALMTOOLCHAIN')

    if (webos_sdk == '')             then webos_sdk = nil             end
    if (webos_pdk == '')             then webos_pdk = nil             end
    if (webos_toolchain_path == '')  then webos_toolchain_path = nil  end
    
    if (not webos_sdk)             then error('You must set the $PALMSDK environment variable to the location of the webos sdk')                  end
    if (not webos_pdk)             then error('You must set the $PALMPDK environment variable to the location of the webos pdk')                  end
    if (not webos_toolchain_path)  then error('You must set the $PALMTOOLCHAIN environment variable to the location of the webos toolchain') end

    local webos_mono = path.join('Binaries', 'webos', 'arm', 'mono')
    if not os.isdir(webos_sdk)             then error('$PALMSDK directory not found: ' .. webos_sdk) end
    if not os.isdir(webos_pdk)             then error('$PALMPDK directory not found: ' .. webos_pdk) end
    if not os.isdir(webos_toolchain_path)  then error('$PALMTOOLCHAIN directory not found: ' .. webos_toolchain_path) end
    if not os.isdir(webos_mono)            then error('mono directory not found: Binaries/webos/arm/mono')   end

    local frameworkpath = path.join(webos_mono, 'lib', 'mono', '4.0')
    compilers.MSNET_CSC_FLAGS   = "'/lib:" .. translate_path_for_tool(frameworkpath) .. "' /nostdlib /r:mscorlib.dll" 
    compilers.Mono_CSC_FLAGS    = compilers.MSNET_CSC_FLAGS
    compilers.Mono_CSC          = "dmcs"

    local webos_pdk_lib     = path.join(webos_pdk, 'device', 'lib')
    local webos_pdk_include = path.join(webos_pdk, 'include')

    if arch.is('arm') then
        compilers.GCC_CC  = path.join(webos_toolchain_path, 'bin', 'arm-none-linux-gnueabi-gcc')
        compilers.GCC_CXX = path.join(webos_toolchain_path, 'bin', 'arm-none-linux-gnueabi-g++')
        compilers.GCC_AR  = path.join(webos_toolchain_path, 'bin', 'arm-none-linux-gnueabi-ar')
        gccflags.Optimize           = "-O -ffast-math"
        compilers.GCC_CC_FLAGS      = "-fPIC -march=armv7-a -mcpu=cortex-a8 -mfloat-abi=softfp -mfpu=neon '-L" .. translate_path_for_tool(webos_pdk_lib) .. "' '-isystem" .. translate_path_for_tool(webos_pdk_include) .. "' -Wl,--allow-shlib-undefined"
        compilers.GCC_CXX_FLAGS     = compilers.GCC_CC_FLAGS
    else
        error('unsupported webos architecture')
    end
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
end

if platform.is('drobo') then
    defines { "PLATFORM_DROBO" }
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
    if arch.is('arm') then
        compilers.GCC_CC  = 'arm-none-linux-gnueabi-gcc'
        compilers.GCC_CXX = 'arm-none-linux-gnueabi-g++'
        compilers.GCC_AR  = 'arm-none-linux-gnueabi-ar'
        compilers.GCC_CC_FLAGS = '-I/usr/arm/include -march=armv5te -L/usr/arm/lib'
        compilers.GCC_CXX_FLAGS = '-I/usr/arm/include -march=armv5te -L/usr/arm/lib'
    else
        error('unsupported drobo architecture')
    end
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
end

if platform.is('android') then
    defines { "PLATFORM_ANDROID" }

    local android_sdk = os.getenv('ANDROID_SDK')
    local android_ndk = os.getenv('ANDROID_NDK')
    local monoandroid_binary_path = os.getenv('MONOANDROID_BINARY_PATH')
    local monoandroid_framework = os.getenv('MONOANDROID_FRAMEWORK_PATH')

    if (android_sdk == '')             then android_sdk = nil             end
    if (android_ndk == '')             then android_ndk = nil             end
    if (monoandroid_binary_path == '') then monoandroid_binary_path = nil end
    if (monoandroid_framework == '')   then monoandroid_framework = nil   end

    if (not android_sdk)             then error('You must set the $ANDROID_SDK environment variable to the location of the android sdk')                             end
    if (not android_ndk)             then error('You must set the $ANDROID_NDK environment variable to the location of the android ndk')                             end
--    if (not monoandroid_binary_path) then error('You must set the $MONOANDROID_BINARY_PATH environment variable to the location of mandroid.exe')                    end
--    if (not monoandroid_framework)   then error('You must set the $MONOANDROID_FRAMEWORK_PATH environment variable to the location of the MonoAndroid framework')     end
    
    if (not os.isdir(android_sdk))             then error('$ANDROID_SDK directory not found: ' .. android_sdk) end
    if (not os.isdir(android_ndk))             then error('$ANDROID_NDK directory not found: ' .. android_ndk) end
--    if (not os.isdir(monoandroid_binary_path)) then error('$MONOANDROID_BINARY_PATH directory not found: ' .. monoandroid_binary_path)  end
--    if (not os.isdir(monoandroid_framework))   then error('$MONOANDROID_FRAMEWORK_PATH directory not found: ' .. monoandroid_framework) end

--    local baseframeworkpath    = path.join(monoandroid_framework, 'v1.0')
--    local currentframeworkpath = path.join(monoandroid_framework, 'v2.3')

    local stl_arch_include_path = path.join(android_ndk, 'sources', 'cxx-stl', 'gnu-libstdc++', 'libs', 'armeabi-v7a', 'include')
    local stl_include_path      = path.join(android_ndk, 'sources', 'cxx-stl', 'gnu-libstdc++', 'include')
    local stl_lib_path          = path.join(android_ndk, 'sources', 'cxx-stl', 'gnu-libstdc++', 'libs', 'armeabi-v7a')

--    if (not os.isdir(baseframeworkpath))    then error('$MONOANDROID_FRAMEWORK_PATH/v1.0 directory not found: ' .. baseframeworkpath)    end
--    if (not os.isdir(currentframeworkpath)) then error('$MONOANDROID_FRAMEWORK_PATH/v2.3 directory not found: ' .. currentframeworkpath) end

    local ndk_host = nil
    if (os.is('windows')) then
        ndk_host = 'windows'
    elseif (os.is('linux')) then
        ndk_host = 'linux-x86'
    elseif (os.is('macosx')) then
        ndk_host = 'darwin-x86'
    else
        error('unsupported os for android ndk builds')
    end

    local ndk_toolchain_dir = path.join(android_ndk, 'toolchains', 'arm-linux-androideabi-4.4.3', 'prebuilt', ndk_host)
    local ndk_sysroot       = path.join(android_ndk, 'platforms', 'android-9', 'arch-arm')
    
    if (not os.isdir(ndk_toolchain_dir)) then error('NDK toolchain not found in ' .. ndk_toolchain_dir) end
    if (not os.isdir(ndk_sysroot))       then error('NDK sysroot not found in ' .. ndk_sysroot)         end

    compilers.GCC_CC            = path.join(ndk_toolchain_dir, 'bin', 'arm-linux-androideabi-gcc')
    compilers.GCC_CXX           = path.join(ndk_toolchain_dir, 'bin', 'arm-linux-androideabi-g++')
    compilers.GCC_AR            = path.join(ndk_toolchain_dir, 'bin', 'arm-linux-androideabi-ar')
    compilers.GCC_CC_FLAGS      = "-marm -march=armv7-a -mfloat-abi=softfp -mfpu=vfpv3-d16 -DARM_FPU_VFP -DVFPv3-D16 '--sysroot=" .. translate_path_for_tool(ndk_sysroot) .. "'"
    compilers.GCC_CXX_FLAGS     = compilers.GCC_CC_FLAGS .. " '-L" .. translate_path_for_tool(stl_lib_path) .. "' -lsupc++ '-isystem" .. translate_path_for_tool(stl_include_path) .. "' '-isystem" .. translate_path_for_tool(stl_arch_include_path) .. "'"
    --compilers.MSNET_CSC_FLAGS   = "'/lib:" .. translate_path_for_tool(baseframeworkpath) .. "' '/lib:" .. translate_path_for_tool(currentframeworkpath) .. "' /nostdlib /r:mscorlib.dll"
    --compilers.Mono_CSC_FLAGS    = compilers.MSNET_CSC_FLAGS
    --compilers.Mono_CSC          = "smcs"

    gccflags.Optimize = "-O"
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
end

if platform.is('linux') then
    compilers.Mono_CSC_DEFINES = { "MONO" }
    compilers.MSNET_CSC_DEFINES = { "MONO" }
    defines { "PLATFORM_LINUX" }
end

if system.is('windows') then
   defines {"SYSTEM_WINDOWS"}
elseif system.is('linux') then
   defines {"SYSTEM_LINUX"}
elseif system.is('macosx') then
   defines {"SYSTEM_MACOSX", "SYSTEM_OSX"}
end

-----------------------------------------------------------------------------------------
-----------------------------------------------------------------------------------------
--
-- Utilities for deailing with Binaries/
--
-----------------------------------------------------------------------------------------
-----------------------------------------------------------------------------------------

function copybinaries_managed(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            copybinary_managed(i)
        end
    else
        copybinary_managed(o)
    end
end

function copybinary_managed(o)
    copyfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", "managed", o)) }
end

function copybinaries_sharedlib(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            copybinary_sharedlib(i)
        end
    else
        copybinary_sharedlib(o)
    end
end

function copybinary_sharedlib(o)
    local prefix = ''
    local suffix = ''

    if system.is('windows') then
        suffix = '.dll'
    elseif system.is('linux') then
        prefix = 'lib'
        suffix = '.so'
    elseif system.is('macosx') then
        prefix = 'lib'
        suffix = '.dylib'
    end

    copyfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", platform.get(), arch.get(), prefix .. o .. suffix)) }
end

function copybinaries_exe(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            copybinary_exe(i)
        end
    else
        copybinary_exe(o)
    end
end

function copybinary_exe(o)
    local suffix = ''

    if system.is('windows') then
        suffix = '.exe'
    end

    copyfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", platform.get(), arch.get(), o .. suffix)) }
end

function linkbinaries_staticlib(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            linkbinary_staticlib(i)
        end
    else
        linkbinary_staticlib(o)
    end
end

function linkbinary_staticlib(o)
    linkfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", platform.get(), arch.get(), o)) }
end

function linkbinaries_sharedlib(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            linkbinary_sharedlib(i)
        end
    else
        linkbinary_sharedlib(o)
    end
end

function linkbinary_sharedlib(o)
    if system.is('windows') then
        linkbinary_staticlib(o)
        copybinary_sharedlib(o)
    else
        local prefix = ''
        local suffix = ''
        if system.is('linux') then
            prefix = 'lib'
            suffix = '.so'
        elseif system.is('macosx') then
            prefix = 'lib'
            suffix = '.dylib'
        end
        linkfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", platform.get(), arch.get(), prefix .. o .. suffix)) }
    end
end

function linkbinaries_managed(o)
    if (type(o) == "table") then
        for _,i in ipairs(o) do
            linkbinary_managed(i)
        end
    else
        linkbinary_managed(o)
    end
end

function linkbinary_managed(o)
    linkfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", "managed", o)) }
end

function linkbinary_managed_platform(o)
    linkfiles { path.getrelative(genmake.current.subdir, path.join("Binaries", platform.get(), "managed", o)) }
end
