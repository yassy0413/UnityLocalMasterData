#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace LocalMasterData
{
    public static class LocalMasterDataCompressor
    {
        public static byte[] CompressAndEncrypt(byte[] inBytes, byte[] iv, byte[] key, byte[] hmacSecretKey)
        {
            inBytes = Compress(inBytes);
            inBytes = Hmac(inBytes, hmacSecretKey);
            inBytes = Encrypt(inBytes, iv, key);
            return inBytes;
        }

        public static byte[] DecompressAndDecrypt(byte[] inBytes, byte[] iv, byte[] key, byte[] hmacSecretKey)
        {
            inBytes = Decrypt(inBytes, iv, key);
            return VerifyHmac(inBytes, hmacSecretKey) ? Decompress(inBytes) : Array.Empty<byte>();
        }

        public static AesManaged CreateAesManaged()
        {
            return new()
            {
                KeySize = 256,
                BlockSize = 128,
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7,
            };
        }

        private static readonly AesManaged AesManaged = CreateAesManaged();

        private static byte[] Compress(byte[] inBytes)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionMode.Compress))
            {
                ds.Write(inBytes, 0, inBytes.Length);
            }

            var outBytes = ms.ToArray();
            Debug.Log($"Compress [{inBytes.Length}] -> [{outBytes.Length}] bytes");
            return outBytes;
        }

        private static byte[] Encrypt(byte[] inBytes, byte[] iv, byte[] key)
        {
            var aes = AesManaged;
            aes.IV = iv;
            aes.Key = key;
            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(inBytes, 0, inBytes.Length);
        }

        private static byte[] Hmac(byte[] inBytes, byte[] secretKey)
        {
            byte[] hmacBytes;
            using (var hmac = new HMACSHA256(secretKey))
            {
                hmacBytes = hmac.ComputeHash(inBytes);
            }

            var outBytes = new byte[inBytes.Length + hmacBytes.Length];
            Buffer.BlockCopy(inBytes, 0, outBytes, 0, inBytes.Length);
            Buffer.BlockCopy(hmacBytes, 0, outBytes, inBytes.Length, hmacBytes.Length);
            return outBytes;
        }

        private static byte[] Decompress(byte[] inBytes)
        {
            using var msIn = new MemoryStream(inBytes);
            using var msOut = new MemoryStream();
            using (var ds = new DeflateStream(msIn, CompressionMode.Decompress))
            {
                ds.CopyTo(msOut);
            }

            var outBytes = msOut.ToArray();
            return outBytes;
        }

        private static byte[] Decrypt(byte[] inBytes, byte[] iv, byte[] key)
        {
            var aes = AesManaged;
            aes.IV = iv;
            aes.Key = key;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(inBytes, 0, inBytes.Length);
        }

        private static bool VerifyHmac(byte[] inBytes, byte[] secretKey)
        {
            byte[] hmacBytes;
            using (var hmac = new HMACSHA256(secretKey))
            {
                hmacBytes = hmac.ComputeHash(inBytes, 0, inBytes.Length - hmac.HashSize / 8);
            }

            return CryptographicOperations.FixedTimeEquals(
                inBytes.AsSpan(inBytes.Length - hmacBytes.Length, hmacBytes.Length), hmacBytes);
        }
    }
}