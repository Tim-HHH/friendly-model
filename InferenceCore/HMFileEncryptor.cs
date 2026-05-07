using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HMManager
{
    internal class FileEncryptor
    {
        private byte[] fixedKey = Encoding.UTF8.GetBytes("Hans_J_AI");

        internal byte[] DecryptFile(string inputFile)
        {
            // 为了调试方便，如果直接是 .onnx 并没有加密，直接返回文件字节
            if (inputFile.EndsWith(".onnx", System.StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(inputFile);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = GetValidKey(fixedKey);
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                using (FileStream fsInput = new FileStream(inputFile, FileMode.Open))
                {
                    byte[] iv = new byte[aesAlg.BlockSize / 8];
                    int bytesRead = fsInput.Read(iv, 0, iv.Length);
                    if (bytesRead != iv.Length) throw new CryptographicException("无效的文件");
                    aesAlg.IV = iv;

                    using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    using (MemoryStream msOutput = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(fsInput, decryptor, CryptoStreamMode.Read))
                        {
                            cs.CopyTo(msOutput);
                        }
                        return msOutput.ToArray();
                    }
                }
            }
        }

        internal byte[] GetValidKey(byte[] key)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(key);
        }
    }
}