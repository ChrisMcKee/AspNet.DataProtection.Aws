﻿// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetCore.DataProtection.Aws.Kms
{
    // ReSharper disable once InheritdocConsiderUsage
    /// <summary>
    /// An ASP.NET key decryptor using AWS KMS
    /// </summary>
    public sealed class KmsXmlDecryptor : IXmlDecryptor
    {
        private readonly ILogger logger;
        private readonly IAmazonKeyManagementService kmsClient;
        private readonly IOptions<KmsXmlEncryptorConfig> config;
        private readonly IOptions<DataProtectionOptions> dpOptions;

        /// <summary>
        /// Creates a <see cref="KmsXmlDecryptor"/> for decrypting ASP.NET keys with a KMS master key
        /// </summary>
        /// <remarks>
        /// DataProtection has a fairly awful way of making the IXmlDecryptor that by default never just does
        /// <see cref="IServiceProvider.GetService"/>, instead calling the constructor that takes <see cref="IServiceProvider"/> directly.
        /// This means we have to do the resolution of needed objects via <see cref="IServiceProvider"/>.
        /// </remarks>
        /// <param name="services">A mandatory <see cref="IServiceProvider"/> to provide services</param>
        public KmsXmlDecryptor(IServiceProvider services)
        {
            kmsClient = services?.GetRequiredService<IAmazonKeyManagementService>() ?? throw new ArgumentNullException(nameof(services));
            config = services.GetRequiredService<IOptions<KmsXmlEncryptorConfig>>();
            dpOptions = services.GetRequiredService<IOptions<DataProtectionOptions>>();
            logger = services.GetService<ILoggerFactory>()?.CreateLogger<KmsXmlDecryptor>();
        }

        /// <summary>
        /// Configuration of how KMS will encrypt the XML data
        /// </summary>
        public IKmsXmlEncryptorConfig Config => config.Value;

        /// <summary>
        /// Ensure configuration is valid for usage.
        /// </summary>
        public void ValidateConfig()
        {
            // Microsoft haven't provided for any validation of options as yet, so what was originally a constructor argument must now be validated by hand at runtime (yuck)
            if(string.IsNullOrWhiteSpace(Config.KeyId))
            {
                throw new ArgumentException($"A key id is required in {nameof(IKmsXmlEncryptorConfig)} for KMS operation");
            }
        }

        /// <inheritdoc/>
        public XElement Decrypt(XElement encryptedElement)
        {
            // https://github.com/dotnet/aspnetcore/issues/3548 sync by design
            return Task.Run(() => DecryptAsync(encryptedElement, CancellationToken.None)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Decrypts a provided XML element.
        /// </summary>
        /// <param name="encryptedElement">Encrypted XML element.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Decrypted XML element.</returns>
#pragma warning disable S3242 // Not altering Microsoft interface definition
        public async Task<XElement> DecryptAsync(XElement encryptedElement, CancellationToken ct)
#pragma warning restore S3242
        {
            ValidateConfig();

            logger?.LogDebug("Decrypting ciphertext DataProtection key using AWS key {0}", Config.KeyId);

            using(var memoryStream = new MemoryStream())
            {
                byte[] protectedKey = Convert.FromBase64String((string)encryptedElement.Element("value"));
                await memoryStream.WriteAsync(protectedKey, 0, protectedKey.Length, ct);
                var kmsContext = ContextUpdater.GetEncryptionContext(Config, dpOptions.Value);

                var response = await kmsClient.DecryptAsync(new DecryptRequest
                {
                    EncryptionContext = kmsContext,
                    GrantTokens = Config.GrantTokens,
                    CiphertextBlob = memoryStream
                }, ct).ConfigureAwait(false);

                // Help indicates that Plaintext might be empty if the key couldn't be retrieved but
                // testing shows that you always get an exception thrown first
                using(var plaintext = response.Plaintext)
                {
                    // Ignoring all the good reasons mentioned in KmsXmlEncryptor and that the implementation would
                    // be error-prone, hard to test & review, as well as vary between NET Full & NET Core, it's not
                    // actually permitted to access the buffer of response.Plaintext because it was populated in
                    // the SDK from a constructor which disallows any subsequent writing.
                    //
                    // Yet more reasons that this needs to be handled at a framework level, providing clear Secure* primitives.
                    return XElement.Load(plaintext);
                }
            }
        }
    }
}
