#!/bin/bash
set -e  # Exit on error

echo "========================================="
echo "ManagedSecurity - Build & Test (NativeAOT)"
echo "========================================="
echo ""

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
dotnet publish ManagedSecurity.Test/ManagedSecurity.Test.csproj \
  -c Release -r linux-x64 --self-contained true /p:PublishAot=true
echo "✓ Test suite published"
echo ""

# 4. Run tests
echo "Running tests..."
echo "========================================="
./ManagedSecurity.Test/bin/Release/net8.0/linux-x64/publish/ManagedSecurity.Test
