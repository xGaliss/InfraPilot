namespace InfraPilot.Capabilities.Abstractions;

using System.Security.Cryptography;
using System.Text;

public static class SnapshotHashing
{
    public static string Compute(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
