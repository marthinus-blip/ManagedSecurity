

## Linux

### Setup Linux
1. The build environment was missing the zlib development headers, which are required for NativeAOT linking on Linux. I resolved this by creating a local symlink to the system's libz.so.1:

    mkdir -p local_lib && ln -s /usr/lib/x86_64-linux-gnu/libz.so.1 local_lib/libz.so

### Build and Run
1. Publish the project as a NativeAOT binary and executed it:

    dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj -c Release -r linux-x64 --self-contained true /p:PublishAot=true