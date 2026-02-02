//  src\KsefGateway.KsefService\Services\EncryptionService.cs
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;

namespace KsefGateway.KsefService.Services
{
    public class EncryptionService
    {
        private readonly HttpClient _httpClient;
        
        // Храним сгенерированный ключ AES, чтобы потом им шифровать файлы (если нужно)
        public byte[] CurrentAesKey { get; private set; }
        public byte[] CurrentIv { get; private set; }

        public EncryptionService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        public async Task<(string EncryptedKey, string Iv)> PrepareSessionKeysAsync(string publicKeyUrl)
        {
            // 1. Генерируем AES ключ (32 байта) и IV (16 байт)
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();

            CurrentAesKey = aes.Key;
            CurrentIv = aes.IV;

            // 2. Скачиваем Публичный Ключ Минфина (PEM/CER)
            // В реальном проекте лучше кэшировать этот ключ, чтобы не качать каждый раз
            var publicKeyPem = await _httpClient.GetStringAsync(publicKeyUrl);
            
            // 3. Шифруем AES-ключ публичным ключом Минфина (RSA OAEP SHA256)
            using var rsa = RSA.Create();
            
            // KSeF обычно отдает ключ в формате PEM X.509
            // Нам нужно вырезать заголовки -----BEGIN... если ImportFromPem не сработает,
            // но в .NET 6+ ImportFromPem работает отлично.
            rsa.ImportFromPem(publicKeyPem);

            var encryptedKeyBytes = rsa.Encrypt(CurrentAesKey, RSAEncryptionPadding.OaepSHA256);

            // 4. Возвращаем Base64 строки для JSON
            return (
                Convert.ToBase64String(encryptedKeyBytes),
                Convert.ToBase64String(CurrentIv)
            );
        }
    }
}