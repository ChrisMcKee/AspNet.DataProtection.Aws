﻿// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using Amazon.S3;
using AspNetCore.DataProtection.Aws.S3;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AspNetCore.DataProtection.Aws.IntegrationTests
{
    public class S3ConfigurationTests : IClassFixture<ConfigurationFixture>
    {
        private readonly ConfigurationFixture fixture;
        private readonly IAmazonS3 s3Client;

        public S3ConfigurationTests(ConfigurationFixture fixture)
        {
            this.fixture = fixture;
            s3Client = new Mock<IAmazonS3>().Object;
        }

        [Fact]
        public void ExpectFullConfigurationBinding()
        {
            var section = fixture.Configuration.GetSection("s3XmlStorageFull");

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDataProtection()
                             .PersistKeysToAwsS3(s3Client, section);
            using(var serviceProvider = serviceCollection.BuildServiceProvider())
            {
                var options = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
                Assert.IsType<S3XmlRepository>(options.XmlRepository);

                var repo = (S3XmlRepository)options.XmlRepository;

                // Values should match config.json section
                Assert.Equal((string)"1234", (string)repo.Config.Bucket);
                Assert.Equal((string)"key", (string)repo.Config.KeyPrefix);
                Assert.Equal<S3StorageClass>(S3StorageClass.Glacier, repo.Config.StorageClass);
                Assert.Equal<ServerSideEncryptionCustomerMethod>(ServerSideEncryptionCustomerMethod.AES256, repo.Config.ServerSideEncryptionCustomerMethod);
                Assert.Equal((string)"provKey", (string)repo.Config.ServerSideEncryptionCustomerProvidedKey);
                Assert.Equal((string)"provKeyMd5", (string)repo.Config.ServerSideEncryptionCustomerProvidedKeyMd5);
                Assert.Equal((string)"servKeyId", (string)repo.Config.ServerSideEncryptionKeyManagementServiceKeyId);
                Assert.Equal<ServerSideEncryptionMethod>(ServerSideEncryptionMethod.AWSKMS, repo.Config.ServerSideEncryptionMethod);
                Assert.Equal(7, repo.Config.MaxS3QueryConcurrency);
#pragma warning disable xUnit2004 // Do not use equality check to test for boolean conditions
                Assert.Equal(false, repo.Config.ClientSideCompression);
                Assert.Equal(true, repo.Config.ValidateETag);
                Assert.Equal(false, repo.Config.ValidateMd5Metadata);
#pragma warning restore xUnit2004
            }
        }
    }
}
