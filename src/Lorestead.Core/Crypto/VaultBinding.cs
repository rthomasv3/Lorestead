using System.Globalization;
using System.Text;

namespace Lorestead.Core.Crypto
{
    /// <summary>
    /// Associated data that ties a ciphertext to the row and field it was written for,
    /// so a ciphertext moved elsewhere fails authentication.
    /// </summary>
    public static class VaultBinding
    {
        public static byte[] ForField(string itemId, string field, int keyVersion)
        {
            return Encode("field", itemId, field, keyVersion.ToString(CultureInfo.InvariantCulture));
        }

        public static byte[] ForKeyRow(string vaultId, string keyRowId, int kind)
        {
            return Encode("key", vaultId, keyRowId, kind.ToString(CultureInfo.InvariantCulture));
        }

        private static byte[] Encode(params string[] parts)
        {
            // NUL never appears in an id or field name, so two part lists cannot encode alike.
            return Encoding.UTF8.GetBytes(string.Join('\0', parts));
        }
    }
}
