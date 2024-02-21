using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace Common.Lib
{
    public class SecurityUtility
    {
        // 암호화 키의 길이가 128bit/192bit/256bit(16byte/24byte/32byte)
        private static readonly string Key = "NicoIsMyWifeabcd";
        //private static readonly string Key = "developer123!@#%developer123!@#%";
        private static readonly byte[] IV = new byte[] { 0, 1, 0, 3, 2, 2, 8, 0, 2, 6, 4, 0, 8, 0, 3, 0 };

        // ******************************************************************************************
        //
        // Date : 
        // Description : AES 암호화
        // Parameters
        //		- [in] string plainText : 암호화할 문자열
        // Return Type : string
        // Reference :
        //
        // ******************************************************************************************		
        public static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText) || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");

            string encryptedData = string.Empty;

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.UTF8.GetBytes(Key);
                aes.IV = IV;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] pbBuffer = null;
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        byte[] pbInputText = Encoding.UTF8.GetBytes(plainText);
                        csEncrypt.Write(pbInputText, 0, pbInputText.Length);
                    }

                    pbBuffer = msEncrypt.ToArray();
                }

                encryptedData = Convert.ToBase64String(pbBuffer);
            }

            return encryptedData;
        }

        // ******************************************************************************************
        //
        // Date : 
        // Description : AES 복호화
        // Parameters
        //		- [in] string cipherText : 복호화할 문자열
        // Return Type : string
        // Reference :
        //
        // ******************************************************************************************		
        public static bool DecryptString(string cipherText, out string result)
        {
            result = "";
            try
            {
                if (string.IsNullOrEmpty(cipherText) || cipherText.Length <= 0)
                    throw new ArgumentNullException("cipherText");

                string decryptedData = string.Empty;

                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = Encoding.UTF8.GetBytes(Key);
                    aes.IV = IV;

                    ICryptoTransform decryptor = aes.CreateDecryptor();
                    byte[] pbBuffer = null;
                    using (MemoryStream msDecrypt = new MemoryStream())
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Write))
                        {
                            byte[] pbEncInputText = Convert.FromBase64String(cipherText);
                            csDecrypt.Write(pbEncInputText, 0, pbEncInputText.Length);
                        }

                        pbBuffer = msDecrypt.ToArray();
                    }

                    decryptedData = Encoding.UTF8.GetString(pbBuffer);
                }
                result = decryptedData;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex);
                return false;
            }

            return true;
        }

        public static void GenerateKeyAndIV(int keySize = 256, int blockSize = 128)
        {
            // This code is only here for an example
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = keySize;
                aes.BlockSize = blockSize;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                aes.GenerateKey();
                aes.GenerateIV();

                string newKey = ByteArrayToHexString(aes.Key);
                string newinitVector = ByteArrayToHexString(aes.IV);
            }
        }

        public static string ByteArrayToHexString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        // AES를 통해
        public static string GetFpID(ulong userNo)
        {
            var guid = "this_is_secretkey_for_userno";
            return EncryptString(userNo + ":" + guid);
        }

        /// <summary>
        /// 유효하지 않으면 0을 리턴한다
        /// </summary>
        /// <param name="FpID"></param>
        /// <returns></returns>
        public static bool GetUserNo(string FpID, out ulong userNo)
        {
            userNo = 0;
            if (SecurityUtility.DecryptString(FpID, out string decrypt) == false)
            {
                return false;
            };
            if (decrypt.Length == 0)
            {
                return false;
            }
            var decrypts = decrypt.Split(":");
            if (decrypts.Length == 0)
            {
                return false;
            }
            if (ulong.TryParse(decrypts[0], out userNo) == false)
            {
                return false;
            }

            return true;
        }
    }
}
