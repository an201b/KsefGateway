//  src\KsefGateway.KsefService\Services\EncryptionService.cs
// src\KsefGateway.KsefService\Services\EncryptionService.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace KsefGateway.KsefService.Services
{
    public class EncryptionService
    {
        // Инициализируем пустыми массивами, чтобы не было Warning CS8618
        public byte[] CurrentAesKey { get; private set; } = Array.Empty<byte>();
        public byte[] CurrentIv { get; private set; } = Array.Empty<byte>();

        public EncryptionService()
        {
            // Генерируем ключи сразу при создании сервиса
            GenerateAesKeys();
        }

        public void GenerateAesKeys()
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();

            CurrentAesKey = aes.Key;
            CurrentIv = aes.IV;
        }

        // Метод принимает уже СКАЧАННУЮ строку ключа (из KsefClient), а не URL
        public (string EncryptedKey, string Iv) PrepareSessionKeys(string publicKeyPem)
        {
            // 1. Очищаем ключ от заголовков и пробелов (Critical!)
            var cleanKey = CleanPem(publicKeyPem);

            try 
            {
                var keyBytes = Convert.FromBase64String(cleanKey);
                using var rsa = RSA.Create();

                // 2. Пытаемся импортировать ключ
                try 
                { 
                    rsa.ImportSubjectPublicKeyInfo(keyBytes, out _); 
                }
                catch 
                {
                    // Fallback: Если это сертификат (X.509)
                    using var cert = X509CertificateLoader.LoadCertificate(keyBytes);
                    using var certRsa = cert.GetRSAPublicKey();
                    if (certRsa == null) throw new Exception("Certificate has no RSA key");
                    
                    var encryptedCert = certRsa.Encrypt(CurrentAesKey, RSAEncryptionPadding.OaepSHA256);
                    return (Convert.ToBase64String(encryptedCert), Convert.ToBase64String(CurrentIv));
                }

                // 3. Шифруем AES-ключ
                var encryptedBytes = rsa.Encrypt(CurrentAesKey, RSAEncryptionPadding.OaepSHA256);

                return (
                    Convert.ToBase64String(encryptedBytes),
                    Convert.ToBase64String(CurrentIv)
                );
            }
            catch (Exception ex)
            {
                throw new Exception($"Encryption failed: {ex.Message}");
            }
        }

        private string CleanPem(string pem)
        {
            return Regex.Replace(pem
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("-----BEGIN CERTIFICATE-----", "")
                .Replace("-----END CERTIFICATE-----", "")
                .Replace("\\n", "")
                .Replace("\n", "")
                .Replace("\r", ""), @"\s+", "");
        }
    }
}