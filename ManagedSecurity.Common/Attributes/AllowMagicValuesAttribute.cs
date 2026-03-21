using System;

namespace ManagedSecurity.Common.Attributes;

/// <summary>
/// Exempts a class or method from the MSG001 (Magic values) governance Roslyn analyzer constraint.
/// Valid execution contexts strictly involve Edge cases where Native interoperability or mapping boundaries 
/// necessitate rigid schema constants natively within behavior blocks.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class AllowMagicValuesAttribute : Attribute
{
}
