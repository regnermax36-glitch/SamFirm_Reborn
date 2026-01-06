#!/bin/bash

# SamFirm Reborn Ultra-Deep Porting System - Linux Build Script
# This script builds the Linux version of the firmware porting system

set -e  # Exit on any error

echo "🐧 SamFirm Reborn - Linux Build Script"
echo "======================================"
echo

# Check if .NET 6.0 SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET 6.0 SDK not found!"
    echo "Please install .NET 6.0 SDK:"
    echo "  Ubuntu/Debian: sudo apt install dotnet-sdk-6.0"
    echo "  CentOS/RHEL: sudo dnf install dotnet-sdk-6.0"
    echo "  Arch: sudo pacman -S dotnet-sdk"
    exit 1
fi

# Check .NET version
DOTNET_VERSION=$(dotnet --version)
echo "✅ Found .NET SDK: $DOTNET_VERSION"

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf ./bin ./obj ./publish
dotnet clean SamFirm_Linux.csproj > /dev/null 2>&1 || true

# Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore SamFirm_Linux.csproj

if [ $? -ne 0 ]; then
    echo "❌ Package restore failed!"
    exit 1
fi

# Build debug version
echo "🔨 Building debug version..."
dotnet build SamFirm_Linux.csproj --configuration Debug --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ Debug build failed!"
    exit 1
fi

# Build release version
echo "🚀 Building release version..."
dotnet build SamFirm_Linux.csproj --configuration Release --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ Release build failed!"
    exit 1
fi

# Create publish directory
mkdir -p publish

# Build self-contained executable for x64
echo "📦 Creating self-contained x64 executable..."
dotnet publish SamFirm_Linux.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:EnableCompressionInSingleFile=true \
    -o ./publish/linux-x64/ \
    --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ x64 publish failed!"
    exit 1
fi

# Build self-contained executable for ARM64 (Raspberry Pi, etc.)
echo "📦 Creating self-contained ARM64 executable..."
dotnet publish SamFirm_Linux.csproj \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:EnableCompressionInSingleFile=true \
    -o ./publish/linux-arm64/ \
    --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ ARM64 publish failed!"
    exit 1
fi

# Make executables executable
chmod +x ./publish/linux-x64/SamFirm_Linux
chmod +x ./publish/linux-arm64/SamFirm_Linux

# Get file sizes
X64_SIZE=$(du -h ./publish/linux-x64/SamFirm_Linux | cut -f1)
ARM64_SIZE=$(du -h ./publish/linux-arm64/SamFirm_Linux | cut -f1)

echo
echo "✅ BUILD COMPLETED SUCCESSFULLY!"
echo "================================"
echo
echo "📁 Build outputs:"
echo "  Debug build:     ./bin/Debug/net6.0/"
echo "  Release build:   ./bin/Release/net6.0/"
echo "  x64 executable:  ./publish/linux-x64/SamFirm_Linux ($X64_SIZE)"
echo "  ARM64 executable: ./publish/linux-arm64/SamFirm_Linux ($ARM64_SIZE)"
echo
echo "🚀 Quick test:"
echo "  ./publish/linux-x64/SamFirm_Linux --help"
echo
echo "📋 Usage examples:"
echo "  # Port firmware"
echo "  ./publish/linux-x64/SamFirm_Linux port --source-model SM-S731B --source-region XEF --base-firmware base.tar.md5 --output ported.tar"
echo
echo "  # Download firmware"
echo "  ./publish/linux-x64/SamFirm_Linux download --model SM-S731B --region XEF --output firmware.tar.md5"
echo
echo "  # Validate firmware"
echo "  ./publish/linux-x64/SamFirm_Linux validate --firmware firmware.tar.md5 --target-model SM-F731B"
echo
echo "  # Check compatibility"
echo "  ./publish/linux-x64/SamFirm_Linux compatibility --source-model SM-S731B --target-model SM-F731B"
echo
echo "⚠️  IMPORTANT: This tool modifies firmware and can brick devices if used incorrectly!"
echo "   Always create backups and ensure you understand the risks before flashing."
echo
echo "🎉 Ready for ultra-deep firmware porting on Linux!"

