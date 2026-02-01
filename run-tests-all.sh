#!/bin/bash
set -e  # Exit on error

echo "========================================="
echo "ManagedSecurity - Build & Test (NativeAOT)"
echo "========================================="
echo ""

# 1. Setup zlib symlink (if needed)
if [ ! -f "local_lib/libz.so" ]; then
    echo "Setting up zlib symlink..."
    mkdir -p local_lib
    ln -s /usr/lib/x86_64-linux-gnu/libz.so.1 local_lib/libz.so
    echo "✓ zlib symlink created"
    echo ""
fi

# 2. Build and pack libraries
echo "Building libraries..."
mkdir -p local-packages
dotnet build ManagedSecurity.Common/ManagedSecurity.Common.csproj -c Release
dotnet build ManagedSecurity.Core/ManagedSecurity.Core.csproj -c Release
echo "✓ Libraries built"
echo ""

echo "Packing libraries..."
dotnet pack ManagedSecurity.Common/ManagedSecurity.Common.csproj -c Release --no-build -o ./local-packages
dotnet pack ManagedSecurity.Core/ManagedSecurity.Core.csproj -c Release --no-build -o ./local-packages
echo "✓ Libraries packed"
echo ""

# 3. Publish test suite as NativeAOT
echo "Publishing test suite (NativeAOT)..."
LIBRARY_PATH=$LIBRARY_PATH:$(pwd)/local_lib \
dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj \
  -c Release -r linux-x64 --self-contained true /p:PublishAot=true
echo "✓ Test suite published"
echo ""

# 4. Run tests
echo "Running tests..."
echo "========================================="
./ManagedSecurity.Test/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.Test
