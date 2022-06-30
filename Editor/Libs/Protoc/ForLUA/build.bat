protoc.exe --plugin=protoc-gen-lua="protoc-gen-lua.bat" --lua_out=%1 --proto_path=%2 %3
ping 127.0.0.1 -n 2 >nul