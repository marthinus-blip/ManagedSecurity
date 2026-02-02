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

## Note:
- Git are used to denote versions with the prefix `v` (e.g. `v0.0.2`)
- Git tags are case sensitive, so use `v0.0.2` instead of `V0.0.2`.