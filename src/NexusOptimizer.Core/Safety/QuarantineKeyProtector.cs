using System.Security.Cryptography;

namespace NexusOptimizer.Core.Safety;

/// <summary>Astrazione interna: DPAPI in produzione, chiave finta solo nei test.</summary>
internal interface IQuarantineKeyProtector
{
    byte[] Protect(byte[] plaintextKey);
    byte[] Unprotect(byte[] protectedKey);
}

internal sealed class DpapiCurrentUserKeyProtector : IQuarantineKeyProtector
{
    public byte[] Protect(byte[] plaintextKey)
        => ProtectedData.Protect(plaintextKey, KeyEntropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedKey)
        => ProtectedData.Unprotect(protectedKey, KeyEntropy, DataProtectionScope.CurrentUser);

    private static readonly byte[] KeyEntropy = "NexusOptimizer.Quarantine.v1"u8.ToArray();
}
