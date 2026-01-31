# ManagedSecurity Library

This library provides core security and common components optimized for **NativeAOT** environments.

## Features
- Fully trimmed and AOT compatible.
- Zero runtime reflection.
- High-performance security primitives.

## NativeAOT Support
This package is marked with `<IsAotCompatible>true</IsAotCompatible>`. When used in a project with `PublishAot=true`, it will not produce trimming warnings.

## Usage
Add this package to your .NET 8+ project:
```bash
dotnet add package ManagedSecurity.Common
```
