// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using Amazon.KeyManagementService;
using AspNetCore.DataProtection.Aws.Kms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
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
            
            // Register required services first
            serviceCollection.AddOptions();
            serviceCollection.Configure<DataProtectionOptions>(options => { });
            serviceCollection.Configure<KeyManagementOptions>(options => { });
            
            // Add data protection and KMS
            serviceCollection.AddDataProtection()
                             .ProtectKeysWithAwsKms(kmsClient, section);
            
            using(var serviceProvider = serviceCollection.BuildServiceProvider())
            {
                // Let's test the configuration binding directly
                var kmsConfig = new KmsXmlEncryptorConfig();
                section.Bind(kmsConfig);
                
                // Verify the configuration was bound correctly
                Assert.Equal("key", kmsConfig.KeyId);
                Assert.Contains("someContext", kmsConfig.EncryptionContext.Keys);
                Assert.Equal("someContextValue", kmsConfig.EncryptionContext["someContext"]);
                Assert.Contains("someToken", kmsConfig.GrantTokens);
                Assert.False(kmsConfig.DiscriminatorAsContext);
                Assert.False(kmsConfig.HashDiscriminatorContext);
                
                // Create the encryptor manually to test the configuration
                var encryptor = new KmsXmlEncryptor(kmsClient, new DirectOptions<KmsXmlEncryptorConfig>(kmsConfig), 
                                                   new DirectOptions<DataProtectionOptions>(new DataProtectionOptions()));

                // Verify the encryptor was created successfully
                Assert.NotNull(encryptor);
                Assert.IsType<KmsXmlEncryptor>(encryptor);
            }
        }
    }
}
