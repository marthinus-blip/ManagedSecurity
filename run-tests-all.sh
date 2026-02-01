#!/bin/bash

# Configuration
TEST_BINARY="./ManagedSecurity.Test/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.Test"

    # name: Setup Linux Build Environment (zlib Mystery)
    #  run: |
        mkdir -p local_lib
        sudo ln -s /usr/lib/x86_64-linux-gnu/libz.so.1 local_lib/libz.so

    # - name: Build and Pack Libraries (Local Feed)
    #  run: |
        mkdir -p local-packages
        # Build first to ensure DLLs exist
        dotnet build ManagedSecurity.Common/ManagedSecurity.Common.csproj -c Release
        dotnet build ManagedSecurity.Core/ManagedSecurity.Core.csproj -c Release
        # Pack using the existing builds
        dotnet pack ManagedSecurity.Common/ManagedSecurity.Common.csproj -c Release --no-build -o ./local-packages
        dotnet pack ManagedSecurity.Core/ManagedSecurity.Core.csproj -c Release --no-build -o ./local-packages

    # - name: Publish NativeAOT (Linux)
    #  run: |
        LIBRARY_PATH=$LIBRARY_PATH:$(pwd)/local_lib \
        dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj \
        -c Release -r linux-x64 --self-contained true /p:PublishAot=true

    # - name: Run AOT Compliance Tests (Linux)
    #  run: 
        ./ManagedSecurity.Test/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.Test

    # - name: Publish Consumer Demo (Linux)
    #  run: |
        LIBRARY_PATH=$LIBRARY_PATH:$(pwd)/local_lib \
        dotnet publish ManagedSecurity.ConsumerDemo/ManagedSecurity.ConsumerDemo.csproj \
        -c Release -r linux-x64 --self-contained true /p:PublishAot=true

    # - name: Run Consumer Demo (Linux)
    #  run: 
        ./ManagedSecurity.ConsumerDemo/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.ConsumerDemo

# Check if the binary exists
if [ ! -f "$TEST_BINARY" ]; then
    echo "Error: Test binary not found at $TEST_BINARY"
    echo "Please build the project first using:"
    echo "dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj -c Release -r linux-x64 --self-contained true /p:PublishAot=true"
    exit 1
fi

# # Run the test
# echo "Running NativeAOT Tests via Microsoft.Testing.Platform..."
# $TEST_BINARY
