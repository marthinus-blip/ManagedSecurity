using System;
using System.IO;

namespace ManagedSecurity.Common;

[ManagedSecurity.Common.Attributes.AllowMagicValues]
public static class Paths
{
    private static string? _runtimeData;

    public static string RuntimeData
    {
        get
        {
            if (_runtimeData == null)
            {
                _runtimeData = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RuntimeData"));
                if (!Directory.Exists(_runtimeData))
                {
                    Directory.CreateDirectory(_runtimeData);
                }
            }
            return _runtimeData;
        }
    }

    public static string GetRuntimePath(string fileName)
    {
        return Path.Combine(RuntimeData, fileName);
    }

    public static string GetVaultPath()
    {
        string path = GetRuntimePath("Vault");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
}
