using Neo.VM;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace Neo.Cryptography
{
    public class Crypto : ICrypto
    {
        public static readonly Crypto Default = new Crypto();

        public byte[] Hash160(byte[] message) => message.Sha256().RIPEMD160();

        public byte[] Hash256(byte[] message) => message.Sha256().Sha256();

        public byte[] Sign(byte[] message, byte[] privateKey, byte[] publicKey)
        {
            var ellipticCurveParameters = this.GetParameters(publicKey, privateKey);
            using (var ecdsa = ECDsa.Create(ellipticCurveParameters))
            {
                return ecdsa.SignData(message, HashAlgorithmName.SHA256);
            }
        }

        public bool VerifySignature(byte[] message, byte[] signature, byte[] publicKey)
        {
            if (publicKey.Length == 33 && (publicKey[0] == 0x02 || publicKey[0] == 0x03))
            {
                try
                {
                    publicKey = ECC.ECPoint
                        .DecodePoint(publicKey, ECC.ECCurve.Secp256r1)
                        .EncodePoint(false)
                        .Skip(1)
                        .ToArray();
                }
                catch
                {
                    return false;
                }
            }
            else if (publicKey.Length == 65 && publicKey[0] == 0x04)
            {
                publicKey = publicKey.Skip(1).ToArray();
            }
            else if (publicKey.Length != 64)
            {
                throw new ArgumentException("Invalid public key.");
            }

            var ellipticCurveParameters = this.GetParameters(publicKey);
            using (var ecdsa = ECDsa.Create(ellipticCurveParameters))
            {
                return ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256);
            }
        }

        private ECParameters GetParameters(byte[] publicKey, byte[] privateKey = null)
        {
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey,
                Q = new ECPoint
                {
                    X = publicKey.Take(32).ToArray(),
                    Y = publicKey.Skip(32).ToArray()
                }
            };

            return parameters;
        }
    }
}
