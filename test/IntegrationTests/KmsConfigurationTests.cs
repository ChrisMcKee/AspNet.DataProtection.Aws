﻿// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using Amazon.KeyManagementService;
using AspNetCore.DataProtection.Aws.Kms;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AspNetCore.DataProtection.Aws.IntegrationTests
{
    public class KmsConfigurationTests : IClassFixture<ConfigurationFixture>
    {
        private readonly ConfigurationFixture fixture;
        private readonly IAmazonKeyManagementService kmsClient;

        public KmsConfigurationTests(ConfigurationFixture fixture)
        {
            this.fixture = fixture;
            kmsClient = new Mock<IAmazonKeyManagementService>().Object;
        }

        [Fact]
        public void ExpectFullConfigurationBinding()
        {
            var section = fixture.Configuration.GetSection("kmsXmlEncryptionFull");

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDataProtection()
                             .ProtectKeysWithAwsKms(kmsClient, section);
            using(var serviceProvider = serviceCollection.BuildServiceProvider())
            {
                var options = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
                Assert.IsType<KmsXmlEncryptor>(options.XmlEncryptor);

                var encryptor = (KmsXmlEncryptor)options.XmlEncryptor;

                // Values should match config.json section
                Assert.Equal((string)"key", (string)encryptor.Config.KeyId);
                Assert.Contains(KmsConstants.DefaultEncryptionContextKey, encryptor.Config.EncryptionContext.Keys);
                Assert.Equal(KmsConstants.DefaultEncryptionContextValue, encryptor.Config.EncryptionContext[KmsConstants.DefaultEncryptionContextKey]);
                Assert.Contains<string>("someContext", encryptor.Config.EncryptionContext.Keys);
                Assert.Equal((string)"someContextValue", (string)encryptor.Config.EncryptionContext["someContext"]);
                Assert.Contains<string>("someToken", encryptor.Config.GrantTokens);
#pragma warning disable xUnit2004 // Do not use equality check to test for boolean conditions
                Assert.Equal(false, encryptor.Config.DiscriminatorAsContext);
                Assert.Equal(false, encryptor.Config.HashDiscriminatorContext);
#pragma warning restore xUnit2004
            }
        }
    }
}
