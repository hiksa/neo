using Neo.Cryptography;
using System;

namespace Neo.Extensions
{
    public static class UInt160Extensions
    {
        public static string ToAddress(this UInt160 scriptHash)
        {
            var data = new byte[21];
            data[0] = ProtocolSettings.Default.AddressVersion;

            Buffer.BlockCopy(scriptHash.ToArray(), 0, data, 1, 20);
            return data.Base58CheckEncode();
        }
    }
}
