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
        private const uint Magic = 0x31444D4C; // LMD1 (little endian)
        private const byte FormatVersion = 1;
        private const int AesKeySize = 32;
        private const int AesIvSize = 16;
        private const int HmacSize = 32;
        private const int HeaderSize = sizeof(uint) + sizeof(byte) + sizeof(byte) + sizeof(ushort) + sizeof(int);
        private const uint PrivateKeyMagic = 0x31534D4C; // LMS1 (little endian)

        private readonly struct Header
        {
            public Header(byte ivLength, ushort signatureLength, int ciphertextLength)
            {
                IvLength = ivLength;
                SignatureLength = signatureLength;
                CiphertextLength = ciphertextLength;
            }

            public byte IvLength { get; }
            public ushort SignatureLength { get; }
            public int CiphertextLength { get; }
        }

        public static byte[] CreateAesKey()
        {
            var key = new byte[AesKeySize];
            FillRandom(key);
            return key;
        }

        public static byte[] CreateHmacKey()
        {
            var key = new byte[64];
            FillRandom(key);
            return key;
        }

        public static byte[] CreateSigningPrivateKey()
        {
            using var rsa = RSA.Create();
            rsa.KeySize = 2048;
            return SerializePrivateKey(rsa.ExportParameters(true));
        }

        public static void GetSigningPublicKey(byte[] signingPrivateKey, out byte[] modulus, out byte[] exponent)
        {
            var parameters = DeserializePrivateKey(signingPrivateKey);
            modulus = parameters.Modulus ??
                      throw new CryptographicException("RSA private key has no modulus.");
            exponent = parameters.Exponent ??
                       throw new CryptographicException("RSA private key has no exponent.");
        }

        public static byte[] CompressEncryptAndSign(
            byte[] inBytes,
            byte[] aesKey,
            byte[] hmacSecretKey,
            byte[] signingPrivateKey,
            bool useRsaSignature = true)
        {
            ValidateAesKey(aesKey);
            ThrowIfNull(inBytes, nameof(inBytes));
            ThrowIfNull(hmacSecretKey, nameof(hmacSecretKey));
            ThrowIfNull(signingPrivateKey, nameof(signingPrivateKey));

            var compressed = Compress(inBytes);
            var iv = new byte[AesIvSize];
            FillRandom(iv);
            var ciphertext = Encrypt(compressed, iv, aesKey);

            using var rsa = useRsaSignature ? RSA.Create() : null;
            if (rsa != null)
            {
                rsa.ImportParameters(DeserializePrivateKey(signingPrivateKey));
            }

            var signatureLength = rsa != null ? checked((ushort)(rsa.KeySize / 8)) : (ushort)0;
            var authenticatedData = CreateAuthenticatedData(iv, ciphertext, signatureLength);
            var hmac = ComputeHmac(authenticatedData, hmacSecretKey);

            var signedData = new byte[authenticatedData.Length + hmac.Length];
            Buffer.BlockCopy(authenticatedData, 0, signedData, 0, authenticatedData.Length);
            Buffer.BlockCopy(hmac, 0, signedData, authenticatedData.Length, hmac.Length);

            if (rsa == null)
            {
                return signedData;
            }

            var signature = rsa.SignData(signedData, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (signature.Length != signatureLength)
            {
                throw new CryptographicException("Unexpected RSA signature length.");
            }

            var result = new byte[signedData.Length + signature.Length];
            Buffer.BlockCopy(signedData, 0, result, 0, signedData.Length);
            Buffer.BlockCopy(signature, 0, result, signedData.Length, signature.Length);
            return result;
        }

        public static byte[] DecryptVerifyAndDecompress(
            byte[] inBytes,
            byte[] aesKey,
            byte[] hmacSecretKey,
            byte[] signingPublicKeyModulus,
            byte[] signingPublicKeyExponent)
        {
            ValidateAesKey(aesKey);
            ThrowIfNull(inBytes, nameof(inBytes));
            ThrowIfNull(hmacSecretKey, nameof(hmacSecretKey));
            ThrowIfNull(signingPublicKeyModulus, nameof(signingPublicKeyModulus));
            ThrowIfNull(signingPublicKeyExponent, nameof(signingPublicKeyExponent));

            var header = ReadHeader(inBytes);
            var authenticatedLength = checked(HeaderSize + header.IvLength + header.CiphertextLength);
            var signedLength = checked(authenticatedLength + HmacSize);
            var expectedLength = checked(signedLength + header.SignatureLength);
            if (inBytes.Length != expectedLength)
            {
                throw new InvalidDataException(
                    $"Invalid local master data length. Expected {expectedLength}, actual {inBytes.Length}.");
            }

            if (header.SignatureLength > 0)
            {
                using var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = signingPublicKeyModulus,
                    Exponent = signingPublicKeyExponent,
                });

                var signedData = inBytes.AsSpan(0, signedLength).ToArray();
                var signature = inBytes.AsSpan(signedLength, header.SignatureLength).ToArray();
                if (!rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new CryptographicException("Local master data signature verification failed.");
                }
            }

            Span<byte> expectedHmac = stackalloc byte[HmacSize];
            using (var hmac = new HMACSHA256(hmacSecretKey))
            {
                if (!hmac.TryComputeHash(inBytes.AsSpan(0, authenticatedLength), expectedHmac, out var bytesWritten) ||
                    bytesWritten != HmacSize)
                {
                    throw new CryptographicException("Failed to compute local master data HMAC.");
                }
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    inBytes.AsSpan(authenticatedLength, HmacSize),
                    expectedHmac))
            {
                throw new CryptographicException("Local master data HMAC verification failed.");
            }

            var iv = inBytes.AsSpan(HeaderSize, header.IvLength).ToArray();
            var ciphertext = inBytes.AsSpan(HeaderSize + header.IvLength, header.CiphertextLength).ToArray();
            return Decompress(Decrypt(ciphertext, iv, aesKey));
        }

        private static byte[] CreateAuthenticatedData(
            byte[] iv,
            byte[] ciphertext,
            ushort signatureLength)
        {
            using var stream = new MemoryStream(HeaderSize + iv.Length + ciphertext.Length);
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(checked((byte)iv.Length));
                writer.Write(signatureLength);
                writer.Write(ciphertext.Length);
                writer.Write(iv);
                writer.Write(ciphertext);
            }

            return stream.ToArray();
        }

        private static Header ReadHeader(byte[] bytes)
        {
            if (bytes.Length < HeaderSize + AesIvSize + HmacSize)
            {
                throw new InvalidDataException("Local master data file is too short.");
            }

            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("Unknown local master data format.");
            }

            var version = reader.ReadByte();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported local master data version: {version}.");
            }

            var ivLength = reader.ReadByte();
            var signatureLength = reader.ReadUInt16();
            var ciphertextLength = reader.ReadInt32();
            if (ivLength != AesIvSize ||
                ciphertextLength <= 0 || ciphertextLength % AesIvSize != 0)
            {
                throw new InvalidDataException("Invalid local master data header.");
            }

            return new Header(ivLength, signatureLength, ciphertextLength);
        }

        private static byte[] Compress(byte[] inBytes)
        {
            using var stream = new MemoryStream();
            using (var deflate = new DeflateStream(stream, CompressionMode.Compress))
            {
                deflate.Write(inBytes, 0, inBytes.Length);
            }

            var outBytes = stream.ToArray();
            Debug.Log($"Compress [{inBytes.Length}] -> [{outBytes.Length}] bytes");
            return outBytes;
        }

        private static byte[] Decompress(byte[] inBytes)
        {
            using var input = new MemoryStream(inBytes);
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                deflate.CopyTo(output);
            }

            return output.ToArray();
        }

        private static byte[] Encrypt(byte[] inBytes, byte[] iv, byte[] key)
        {
            using var aes = CreateAes(iv, key);
            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(inBytes, 0, inBytes.Length);
        }

        private static byte[] Decrypt(byte[] inBytes, byte[] iv, byte[] key)
        {
            using var aes = CreateAes(iv, key);
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(inBytes, 0, inBytes.Length);
        }

        private static Aes CreateAes(byte[] iv, byte[] key)
        {
            var aes = Aes.Create();
            aes.KeySize = AesKeySize * 8;
            aes.BlockSize = AesIvSize * 8;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }

        private static byte[] ComputeHmac(ReadOnlySpan<byte> bytes, byte[] secretKey)
        {
            using var hmac = new HMACSHA256(secretKey);
            var result = new byte[HmacSize];
            if (!hmac.TryComputeHash(bytes, result, out var bytesWritten) || bytesWritten != HmacSize)
            {
                throw new CryptographicException("Failed to compute local master data HMAC.");
            }

            return result;
        }

        private static byte[] SerializePrivateKey(RSAParameters parameters)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(PrivateKeyMagic);
                WriteParameter(writer, parameters.Modulus, nameof(parameters.Modulus));
                WriteParameter(writer, parameters.Exponent, nameof(parameters.Exponent));
                WriteParameter(writer, parameters.D, nameof(parameters.D));
                WriteParameter(writer, parameters.P, nameof(parameters.P));
                WriteParameter(writer, parameters.Q, nameof(parameters.Q));
                WriteParameter(writer, parameters.DP, nameof(parameters.DP));
                WriteParameter(writer, parameters.DQ, nameof(parameters.DQ));
                WriteParameter(writer, parameters.InverseQ, nameof(parameters.InverseQ));
            }

            return stream.ToArray();
        }

        private static RSAParameters DeserializePrivateKey(byte[] bytes)
        {
            ThrowIfNull(bytes, nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            if (stream.Length < sizeof(uint) || reader.ReadUInt32() != PrivateKeyMagic)
            {
                throw new CryptographicException("Unknown RSA private key format.");
            }

            var result = new RSAParameters
            {
                Modulus = ReadParameter(reader),
                Exponent = ReadParameter(reader),
                D = ReadParameter(reader),
                P = ReadParameter(reader),
                Q = ReadParameter(reader),
                DP = ReadParameter(reader),
                DQ = ReadParameter(reader),
                InverseQ = ReadParameter(reader),
            };
            if (stream.Position != stream.Length)
            {
                throw new CryptographicException("The RSA private key contains trailing data.");
            }

            return result;
        }

        private static void WriteParameter(BinaryWriter writer, byte[]? value, string name)
        {
            if (value == null || value.Length == 0)
            {
                throw new CryptographicException($"RSA private key has no {name}.");
            }

            writer.Write(checked((ushort)value.Length));
            writer.Write(value);
        }

        private static byte[] ReadParameter(BinaryReader reader)
        {
            if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(ushort))
            {
                throw new CryptographicException("The RSA private key is truncated.");
            }

            var length = reader.ReadUInt16();
            var value = reader.ReadBytes(length);
            if (length == 0 || value.Length != length)
            {
                throw new CryptographicException("The RSA private key is truncated.");
            }

            return value;
        }

        private static void ValidateAesKey(byte[] key)
        {
            ThrowIfNull(key, nameof(key));
            if (key.Length != AesKeySize)
            {
                throw new ArgumentException($"AES key must be {AesKeySize} bytes.", nameof(key));
            }
        }

        private static void ThrowIfNull(object? value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }
        }

        private static void FillRandom(byte[] bytes)
        {
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
        }
    }
}
