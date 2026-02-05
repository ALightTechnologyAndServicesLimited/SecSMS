using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;

namespace SecSMS.Common
{
    public class CryptHelper
    {
        private RSA rsa;

        public byte[] KEY { get; set; }
        public byte[] IV { get; set; }

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

        #endregion

        #region RSA
        public void GenerateRSAKeyPair()
        {
            rsa = RSA.Create();
        }

        public string GetPublicKey()
        {
            return rsa.ToXmlString(false);
        }

        public string DecryptRSA(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;

            try
            {
                var bytes = Convert.FromBase64String(text);
                byte[] decryptedBytes;

                decryptedBytes = rsa.Decrypt(bytes, RSAEncryptionPadding.OaepSHA512);

                var plainText = UTF8Encoding.UTF8.GetString(decryptedBytes);
                return RemoveSaltFromText(plainText);
            }
            catch (Exception ex)
            {
                throw;
                //Console.WriteLine(ex.Message);
            }

            return string.Empty;
        }

        public string EncryptRSA(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;

            try
            {
                text = GetTextAndSalt(text);
                var bytes = UTF8Encoding.UTF8.GetBytes(text);
                byte[] encryptedByte = null;

                encryptedByte = rsa.Encrypt(bytes, RSAEncryptionPadding.OaepSHA512);

                var encryptedText = Convert.ToBase64String(encryptedByte);
                return encryptedText;
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.Message);
                throw;
            }

            return string.Empty;
        }

        public bool InitializeRSA(string publicKey)
        {
            try
            {
                if (rsa == null)
                {
                    GenerateRSAKeyPair();
                }

                rsa.FromXmlString(publicKey);
                return true;
            }
            catch (Exception e)
            {
                throw;
                //Console.WriteLine(e.Message);
            }

            return false;
        }

        private void ValidateRSA()
        {
            if (rsa == null) { throw new Exception("RSA not initialized."); }
            ;
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
}
