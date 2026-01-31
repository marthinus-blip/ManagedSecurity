#!/bin/bash

# Configuration
TEST_BINARY="./ManagedSecurity.Test/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.Test"

# Check if the binary exists
if [ ! -f "$TEST_BINARY" ]; then
    echo "Error: Test binary not found at $TEST_BINARY"
    echo "Please build the project first using:"
    echo "dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj -c Release -r linux-x64 --self-contained true /p:PublishAot=true"
    exit 1
fi

# Run the test
echo "Running NativeAOT Tests via Microsoft.Testing.Platform..."
$TEST_BINARY
