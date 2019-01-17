﻿using System;
using System.Linq;
using System.Text;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.SmartContract;

namespace Neo.Wallets
{
    public class KeyPair : IEquatable<KeyPair>
    {
        public readonly byte[] PrivateKey;
        public readonly Cryptography.ECC.ECPoint PublicKey;

        public KeyPair(byte[] privateKey)
        {
            if (privateKey.Length != 32 && privateKey.Length != 96 && privateKey.Length != 104)
            {
                throw new ArgumentException();
            }

            this.PrivateKey = new byte[32];
            Buffer.BlockCopy(privateKey, privateKey.Length - 32, this.PrivateKey, 0, 32);
            if (privateKey.Length == 32)
            {
                this.PublicKey = Cryptography.ECC.ECCurve.Secp256r1.G * privateKey;
            }
            else
            {
                this.PublicKey = Cryptography.ECC.ECPoint.FromBytes(privateKey, Cryptography.ECC.ECCurve.Secp256r1);
            }
        }

        public UInt160 PublicKeyHash => this.PublicKey.EncodePoint(true).ToScriptHash();

        public bool Equals(KeyPair other)
        {
            if (object.ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return this.PublicKey.Equals(other.PublicKey);
        }

        public override bool Equals(object obj) => this.Equals(obj as KeyPair);

        public string Export()
        {
            var data = new byte[34];
            data[0] = 0x80;

            Buffer.BlockCopy(this.PrivateKey, 0, data, 1, 32);
            data[33] = 0x01;

            var wif = data.Base58CheckEncode();
            Array.Clear(data, 0, data.Length);

            return wif;
        }

        public string Export(string passphrase, int N = 16384, int r = 8, int p = 8)
        {
            var scriptHash = Contract.CreateSignatureRedeemScript(this.PublicKey).ToScriptHash();
            var address = scriptHash.ToAddress();

            var addressHash = Encoding.ASCII.GetBytes(address).Sha256().Sha256().Take(4).ToArray();
            var derivedKey = SCrypt.DeriveKey(Encoding.UTF8.GetBytes(passphrase), addressHash, N, r, p, 64);
            var derivedKeyFirstHalf = derivedKey.Take(32).ToArray();
            var derivedKeySecondHalf = derivedKey.Skip(32).ToArray();
            var encryptedkey = KeyPair.XOR(this.PrivateKey, derivedKeyFirstHalf).AES256Encrypt(derivedKeySecondHalf);

            var buffer = new byte[39];
            buffer[0] = 0x01;
            buffer[1] = 0x42;
            buffer[2] = 0xe0;

            Buffer.BlockCopy(addressHash, 0, buffer, 3, addressHash.Length);
            Buffer.BlockCopy(encryptedkey, 0, buffer, 7, encryptedkey.Length);

            return buffer.Base58CheckEncode();
        }

        public override int GetHashCode() => this.PublicKey.GetHashCode();

        public override string ToString() => this.PublicKey.ToString();

        private static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                throw new ArgumentException();
            }

            return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
        }
    }
}
