using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace SecSMS.Common
{
    public class CryptHelper
    {
        private const string ProtocolVersion = "1";
        private const string MlKemParameterSetName = "ML-KEM-768";
        private const int AesKeySizeBytes = 32;
        private const int AesNonceSizeBytes = 12;
        private const int AesTagSizeBytes = 16;

        private readonly SecureRandom secureRandom = new SecureRandom();
        private X25519PrivateKeyParameters? x25519PrivateKey;
        private MLKemPrivateKeyParameters? mlKemPrivateKey;
        private PqKeyBundle? activeKeyBundle;

        public byte[] KEY { get; set; } = Array.Empty<byte>();
        public byte[] IV { get; set; } = Array.Empty<byte>();

        #region Static Utility Methods
        //public static string GetSensitiveText(bool trimText = true)
        //{
        //    StringBuilder password = new StringBuilder();
        //    ConsoleKeyInfo keyInfo = Console.ReadKey(true);

        //    while (keyInfo.Key != ConsoleKey.Enter)
        //    {
        //        password.Append(keyInfo.KeyChar);

        //        keyInfo = Console.ReadKey(true);
        //    }

        //    var retVal = password.ToString();

        //    if (trimText) retVal = retVal.Trim();
        //    return retVal;
        //}

        private static string RemoveSaltFromText(string plainText)
        {
            int skipLength = plainText.Length / 3;
            return plainText.Substring(0, plainText.Length - skipLength);
        }

        private static string GetRandomSalt(int length)
        {
            if (length == 0) return String.Empty;

            var b = new byte[length];
            RandomNumberGenerator.Create().GetNonZeroBytes(b);

            var str = Convert.ToBase64String(b);
            return str.Substring(0, length);
            return String.Empty;
        }

        private static string GetTextAndSalt(string text)
        {
            int saltLength = text.Length / 2;
            text = text + GetRandomSalt(saltLength);
            return text;
        }

        private static byte[] BuildAssociatedData(PqKeyBundle keyBundle, string senderEphemeralPublicKey)
        {
            var associatedData = string.Join(
                "|",
                keyBundle.Version,
                keyBundle.MlKemParameterSet,
                keyBundle.X25519PublicKey,
                keyBundle.MlKemPublicKey,
                senderEphemeralPublicKey);

            return Encoding.UTF8.GetBytes(associatedData);
        }

        private static byte[] DeriveEncryptionKey(byte[] classicalSecret, byte[] postQuantumSecret, byte[] associatedData)
        {
            var inputKeyingMaterial = new byte[classicalSecret.Length + postQuantumSecret.Length];
            Buffer.BlockCopy(classicalSecret, 0, inputKeyingMaterial, 0, classicalSecret.Length);
            Buffer.BlockCopy(postQuantumSecret, 0, inputKeyingMaterial, classicalSecret.Length, postQuantumSecret.Length);

            try
            {
                var salt = SHA256.HashData(associatedData);
                var info = Encoding.UTF8.GetBytes("SecSMS-PQXDH-AES256GCM");
                return HkdfSha256(inputKeyingMaterial, salt, info, AesKeySizeBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inputKeyingMaterial);
            }
        }

        private static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int outputLength)
        {
            byte[] pseudorandomKey;
            using (var extractHmac = new HMACSHA256(salt))
            {
                pseudorandomKey = extractHmac.ComputeHash(ikm);
            }

            try
            {
                var output = new byte[outputLength];
                var previousBlock = Array.Empty<byte>();
                var offset = 0;
                byte counter = 1;

                while (offset < outputLength)
                {
                    var blockInput = new byte[previousBlock.Length + info.Length + 1];
                    Buffer.BlockCopy(previousBlock, 0, blockInput, 0, previousBlock.Length);
                    Buffer.BlockCopy(info, 0, blockInput, previousBlock.Length, info.Length);
                    blockInput[blockInput.Length - 1] = counter;

                    using var expandHmac = new HMACSHA256(pseudorandomKey);
                    previousBlock = expandHmac.ComputeHash(blockInput);

                    var bytesToCopy = Math.Min(previousBlock.Length, outputLength - offset);
                    Buffer.BlockCopy(previousBlock, 0, output, offset, bytesToCopy);
                    offset += bytesToCopy;
                    counter++;
                }

                if (previousBlock.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(previousBlock);
                }

                return output;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pseudorandomKey);
            }
        }

        private static MLKemParameters GetMlKemParameters(string? parameterSetName = null)
        {
            return parameterSetName switch
            {
                null => MLKemParameters.ml_kem_768,
                "ML-KEM-512" => MLKemParameters.ml_kem_512,
                "ML-KEM-768" => MLKemParameters.ml_kem_768,
                "ML-KEM-1024" => MLKemParameters.ml_kem_1024,
                _ => throw new CryptographicException($"Unsupported ML-KEM parameter set '{parameterSetName}'."),
            };
        }

        #endregion

        #region PQXDH
        public PqKeyBundle CreatePqKeyBundle()
        {
            var mlKemParameters = GetMlKemParameters(MlKemParameterSetName);
            var keyGenParameters = new MLKemKeyGenerationParameters(secureRandom, mlKemParameters);
            var generator = new MLKemKeyPairGenerator();
            generator.Init(keyGenParameters);

            var mlKemKeyPair = generator.GenerateKeyPair();
            var mlKemPublicKey = (MLKemPublicKeyParameters)mlKemKeyPair.Public;
            mlKemPrivateKey = (MLKemPrivateKeyParameters)mlKemKeyPair.Private;

            x25519PrivateKey = new X25519PrivateKeyParameters(secureRandom);
            var x25519PublicKey = x25519PrivateKey.GeneratePublicKey();

            activeKeyBundle = new PqKeyBundle
            {
                Version = ProtocolVersion,
                MlKemParameterSet = MlKemParameterSetName,
                MlKemPublicKey = Convert.ToBase64String(mlKemPublicKey.GetEncoded()),
                X25519PublicKey = Convert.ToBase64String(x25519PublicKey.GetEncoded()),
            };

            return activeKeyBundle;
        }

        public string ExportPqKeyBundle()
        {
            var keyBundle = CreatePqKeyBundle();
            return JsonSerializer.Serialize(keyBundle, PqJsonSerializerContext.Default.PqKeyBundle);
        }

        public PqEncryptedMessage EncryptPq(string text, PqKeyBundle keyBundle)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new CryptographicException("Plaintext is required for encryption.");
            }

            var classicalSecret = new byte[X25519PrivateKeyParameters.SecretSize];
            byte[]? postQuantumSecret = null;
            byte[]? mlKemCipherText = null;
            byte[]? associatedData = null;
            byte[]? encryptionKey = null;
            byte[]? nonce = null;
            byte[]? plainBytes = null;
            byte[]? cipherBytes = null;
            byte[]? tag = null;

            try
            {
                var remoteX25519PublicKey = new X25519PublicKeyParameters(Convert.FromBase64String(keyBundle.X25519PublicKey));
                var senderX25519PrivateKey = new X25519PrivateKeyParameters(secureRandom);
                var senderX25519PublicKey = senderX25519PrivateKey.GeneratePublicKey();
                senderX25519PrivateKey.GenerateSecret(remoteX25519PublicKey, classicalSecret, 0);

                var mlKemParameters = GetMlKemParameters(keyBundle.MlKemParameterSet);
                var remoteMlKemPublicKey = MLKemPublicKeyParameters.FromEncoding(
                    mlKemParameters,
                    Convert.FromBase64String(keyBundle.MlKemPublicKey));

                var encapsulator = new MLKemEncapsulator(mlKemParameters);
                encapsulator.Init(new ParametersWithRandom(remoteMlKemPublicKey, secureRandom));
                mlKemCipherText = new byte[encapsulator.EncapsulationLength];
                postQuantumSecret = new byte[encapsulator.SecretLength];
                encapsulator.Encapsulate(mlKemCipherText, 0, mlKemCipherText.Length, postQuantumSecret, 0, postQuantumSecret.Length);

                var senderEphemeralPublicKey = Convert.ToBase64String(senderX25519PublicKey.GetEncoded());
                associatedData = BuildAssociatedData(keyBundle, senderEphemeralPublicKey);
                encryptionKey = DeriveEncryptionKey(classicalSecret, postQuantumSecret, associatedData);

                nonce = RandomNumberGenerator.GetBytes(AesNonceSizeBytes);
                plainBytes = Encoding.UTF8.GetBytes(GetTextAndSalt(text));
                cipherBytes = new byte[plainBytes.Length];
                tag = new byte[AesTagSizeBytes];

                using var aesGcm = new AesGcm(encryptionKey);
                aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag, associatedData);

                return new PqEncryptedMessage
                {
                    Version = ProtocolVersion,
                    MlKemParameterSet = keyBundle.MlKemParameterSet,
                    X25519EphemeralPublicKey = senderEphemeralPublicKey,
                    MlKemCipherText = Convert.ToBase64String(mlKemCipherText),
                    Nonce = Convert.ToBase64String(nonce),
                    CipherText = Convert.ToBase64String(cipherBytes),
                    Tag = Convert.ToBase64String(tag),
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(classicalSecret);

                if (postQuantumSecret != null)
                {
                    CryptographicOperations.ZeroMemory(postQuantumSecret);
                }

                if (associatedData != null)
                {
                    CryptographicOperations.ZeroMemory(associatedData);
                }

                if (encryptionKey != null)
                {
                    CryptographicOperations.ZeroMemory(encryptionKey);
                }

                if (plainBytes != null)
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
        }

        public string DecryptPq(PqEncryptedMessage encryptedMessage)
        {
            ValidatePqState();

            if (activeKeyBundle == null || x25519PrivateKey == null || mlKemPrivateKey == null)
            {
                throw new CryptographicException("Post-quantum key state is not initialized.");
            }

            var classicalSecret = new byte[X25519PrivateKeyParameters.SecretSize];
            byte[]? postQuantumSecret = null;
            byte[]? associatedData = null;
            byte[]? encryptionKey = null;
            byte[]? plainBytes = null;

            try
            {
                var remoteX25519PublicKey = new X25519PublicKeyParameters(Convert.FromBase64String(encryptedMessage.X25519EphemeralPublicKey));
                x25519PrivateKey.GenerateSecret(remoteX25519PublicKey, classicalSecret, 0);

                var mlKemParameters = GetMlKemParameters(encryptedMessage.MlKemParameterSet);
                var decapsulator = new MLKemDecapsulator(mlKemParameters);
                decapsulator.Init(mlKemPrivateKey);
                var mlKemCipherText = Convert.FromBase64String(encryptedMessage.MlKemCipherText);
                postQuantumSecret = new byte[decapsulator.SecretLength];
                decapsulator.Decapsulate(mlKemCipherText, 0, mlKemCipherText.Length, postQuantumSecret, 0, postQuantumSecret.Length);

                associatedData = BuildAssociatedData(activeKeyBundle, encryptedMessage.X25519EphemeralPublicKey);
                encryptionKey = DeriveEncryptionKey(classicalSecret, postQuantumSecret, associatedData);

                var nonce = Convert.FromBase64String(encryptedMessage.Nonce);
                var cipherBytes = Convert.FromBase64String(encryptedMessage.CipherText);
                var tag = Convert.FromBase64String(encryptedMessage.Tag);
                plainBytes = new byte[cipherBytes.Length];

                using var aesGcm = new AesGcm(encryptionKey);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes, associatedData);

                var plainText = Encoding.UTF8.GetString(plainBytes);
                return RemoveSaltFromText(plainText);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(classicalSecret);

                if (postQuantumSecret != null)
                {
                    CryptographicOperations.ZeroMemory(postQuantumSecret);
                }

                if (associatedData != null)
                {
                    CryptographicOperations.ZeroMemory(associatedData);
                }

                if (encryptionKey != null)
                {
                    CryptographicOperations.ZeroMemory(encryptionKey);
                }

                if (plainBytes != null)
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
        }

        public static PqKeyBundle DeserializePqKeyBundle(string json)
        {
            var keyBundle = JsonSerializer.Deserialize(json, PqJsonSerializerContext.Default.PqKeyBundle);
            if (keyBundle == null)
            {
                throw new CryptographicException("Unable to parse post-quantum key bundle.");
            }

            return keyBundle;
        }

        public static string SerializePqKeyBundle(PqKeyBundle keyBundle)
        {
            return JsonSerializer.Serialize(keyBundle, PqJsonSerializerContext.Default.PqKeyBundle);
        }

        public static PqEncryptedMessage DeserializePqEncryptedMessage(string json)
        {
            var encryptedMessage = JsonSerializer.Deserialize(json, PqJsonSerializerContext.Default.PqEncryptedMessage);
            if (encryptedMessage == null)
            {
                throw new CryptographicException("Unable to parse encrypted post-quantum payload.");
            }

            return encryptedMessage;
        }

        public static string SerializePqEncryptedMessage(PqEncryptedMessage encryptedMessage)
        {
            return JsonSerializer.Serialize(encryptedMessage, PqJsonSerializerContext.Default.PqEncryptedMessage);
        }

        private void ValidatePqState()
        {
            if (x25519PrivateKey == null || mlKemPrivateKey == null || activeKeyBundle == null)
            {
                throw new CryptographicException("Post-quantum key bundle not initialized.");
            }
        }

        #endregion

        #region TripleDES

        //public void GenerateNewTripleDES()
        //{
        //    var tripleDES = TripleDES.Create();
        //    tripleDES.GenerateIV();
        //    tripleDES.GenerateKey();

        //    IV = tripleDES.IV;
        //    KEY = tripleDES.Key;
        //}

        //public string GetEncryptedTripleDESKey()
        //{
        //    ValidateRSA();

        //    return EncryptRSA(Convert.ToBase64String(KEY));
        //}

        //public string GetEncryptedTripleDESIV()
        //{
        //    ValidateRSA();

        //    return EncryptRSA(Convert.ToBase64String(IV));
        //}

        //public void ImportTripleDES(string encKey, string encIV)
        //{
        //    ValidateRSA();
        //    //GenerateNewTripleDES();
        //    var keyTxt = DecryptRSA(encKey);
        //    KEY = Convert.FromBase64String(keyTxt);
        //    var ivTxt = DecryptRSA(encIV);
        //    IV = Convert.FromBase64String(ivTxt);

        //    Console.WriteLine("Key & IV have been imported.");
        //}


        //public string EncryptTripleDES(string plainText)
        //{
        //    plainText = GetTextAndSalt(plainText);
        //    byte[] encrypted;
        //    using (TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider())
        //    {
        //        ICryptoTransform encryptor = tdes.CreateEncryptor(KEY, IV);
        //        using (MemoryStream ms = new MemoryStream())
        //        {
        //            using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        //            {
        //                using (StreamWriter sw = new StreamWriter(cs))
        //                    sw.Write(plainText);
        //                encrypted = ms.ToArray();
        //            }
        //        }
        //    }
        //    return Convert.ToBase64String(encrypted);
        //}


        //public string DecryptTripleDES(string cipherText)
        //{
        //    string plaintext = null;
        //    var cipherBytes = Convert.FromBase64String(cipherText);
        //    using (TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider())
        //    {
        //        ICryptoTransform decryptor = tdes.CreateDecryptor(KEY, IV);
        //        using (MemoryStream ms = new MemoryStream(cipherBytes))
        //        {
        //            using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
        //            {
        //                using (StreamReader reader = new StreamReader(cs))
        //                    plaintext = reader.ReadToEnd();
        //            }
        //        }
        //    }

        //    plaintext = RemoveSaltFromText(plaintext);
        //    return plaintext;
        //}

        #endregion
    }

    public sealed class PqKeyBundle
    {
        public string Version { get; set; } = string.Empty;

        public string MlKemParameterSet { get; set; } = string.Empty;

        public string MlKemPublicKey { get; set; } = string.Empty;

        public string X25519PublicKey { get; set; } = string.Empty;
    }

    public sealed class PqEncryptedMessage
    {
        public string Version { get; set; } = string.Empty;

        public string MlKemParameterSet { get; set; } = string.Empty;

        public string X25519EphemeralPublicKey { get; set; } = string.Empty;

        public string MlKemCipherText { get; set; } = string.Empty;

        public string Nonce { get; set; } = string.Empty;

        public string CipherText { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;
    }

    [JsonSerializable(typeof(PqKeyBundle))]
    [JsonSerializable(typeof(PqEncryptedMessage))]
    internal partial class PqJsonSerializerContext : JsonSerializerContext
    {
    }
}
