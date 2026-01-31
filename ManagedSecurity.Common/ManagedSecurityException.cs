namespace ManagedSecurity.Common;

[System.Serializable]
public class ManagedSecurityException : System.Exception {
    public ManagedSecurityException() { }
    public ManagedSecurityException(string message) : base(message) { }
    public ManagedSecurityException(string message, System.Exception inner) : base(message, inner) { }
}
