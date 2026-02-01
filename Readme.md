
## Linux NativeAOT Build

### 1. Environment Setup
The build requires `zlib` development headers, which may be missing in some environments. You can resolve this by providing a local symlink to the system's `libz.so.1`:

```bash
mkdir -p local_lib
ln -s /usr/lib/x86_64-linux-gnu/libz.so.1 local_lib/libz.so
```

### 2. Build and Execute
Publish the project as a NativeAOT binary. This project uses the modern `MSTest.Sdk` which integrates with `Microsoft.Testing.Platform` for high-performance, AOT-compliant test execution.

```bash
# Publish for Linux x64
dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj -c Release -r linux-x64 --self-contained true /p:PublishAot=true

# Run the AOT compliance tests
./run-tests-all.sh
```