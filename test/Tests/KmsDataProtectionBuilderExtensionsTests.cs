// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.KeyManagementService;
using AspNetCore.DataProtection.Aws.Kms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FakeItEasy;
using Xunit;
using DataProtectionBuilderExtensions = AspNetCore.DataProtection.Aws.Kms.DataProtectionBuilderExtensions;

namespace AspNetCore.DataProtection.Aws.Tests
{
    public class KmsDataProtectionBuilderExtensionsTests
    {
        private readonly IDataProtectionBuilder builder;
        private readonly IServiceCollection svcCollection;
        private readonly IAmazonKeyManagementService client;
        private readonly IServiceProvider provider;
        private readonly ILoggerFactory loggerFactory;
        private readonly IOptions<KmsXmlEncryptorConfig> snapshot;
        private readonly IOptions<DataProtectionOptions> dpSnapshot;

        public KmsDataProtectionBuilderExtensionsTests()
        {
            builder = A.Fake<IDataProtectionBuilder>(o => o.Strict());
            client = A.Fake<IAmazonKeyManagementService>(o => o.Strict());
            svcCollection = A.Fake<IServiceCollection>(o => o.Strict());
            provider = A.Fake<IServiceProvider>(o => o.Strict());
            loggerFactory = A.Fake<ILoggerFactory>(o => o.Strict());
            snapshot = A.Fake<IOptions<KmsXmlEncryptorConfig>>(o => o.Strict());
            dpSnapshot = A.Fake<IOptions<DataProtectionOptions>>(o => o.Strict());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExpectBuilderAdditions(bool withClient)
        {
            IServiceCollection services = new ServiceCollection();
            A.CallTo(() => builder.Services).Returns(svcCollection);
            A.CallTo(() => svcCollection.GetEnumerator()).ReturnsLazily(() => services.GetEnumerator());
            A.CallTo(() => svcCollection.Add(A<ServiceDescriptor>._))
             .Invokes((ServiceDescriptor sd) => { services.TryAdd(sd); });
            A.CallTo(() => svcCollection.Count).Returns(services.Count);

            var config = new KmsXmlEncryptorConfig("keyId");

            // Repeat call to ensure cumulative calls work
            if(withClient)
            {
                builder.ProtectKeysWithAwsKms(client, config);
                builder.ProtectKeysWithAwsKms(client, config);
            }
            else
            {
                builder.ProtectKeysWithAwsKms(config);
                builder.ProtectKeysWithAwsKms(config);

                // We haven't passed in an aws key-management service and we don't have one registered for it to pick up at registration.
                Assert.Equal(0, services.Count(x => x.ServiceType == typeof(IAmazonKeyManagementService)));
            }

            Assert.Equal(withClient ? 1 : 0, services.Count(x => x.ServiceType == typeof(IAmazonKeyManagementService)));

            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)));
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)));

            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)).Lifetime);

            A.CallTo(() => provider.GetService(typeof(IAmazonKeyManagementService))).Returns(client);

            if(withClient)
            {
                Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IAmazonKeyManagementService)).Lifetime);
                Assert.Same(client, services.Single(x => x.ServiceType == typeof(IAmazonKeyManagementService)).ImplementationInstance);
            }

            // Ensure we run equivalent config for the actual configuration object
            var configureObject = services.First(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)).ImplementationInstance;
            var optionsObject = new KmsXmlEncryptorConfig();
            ((IConfigureOptions<KmsXmlEncryptorConfig>)configureObject)?.Configure(optionsObject);

            A.CallTo(() => provider.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);
            A.CallTo(() => provider.GetService(typeof(IOptions<KmsXmlEncryptorConfig>))).Returns(snapshot);
            A.CallTo(() => provider.GetService(typeof(IOptions<DataProtectionOptions>))).Returns(dpSnapshot);
            A.CallTo(() => loggerFactory.CreateLogger(typeof(KmsXmlEncryptor).FullName)).Returns(A.Fake<ILogger<KmsXmlEncryptor>>());
            A.CallTo(() => snapshot.Value).Returns(optionsObject);
            var dbOptions = new DataProtectionOptions();
            A.CallTo(() => dpSnapshot.Value).Returns(dbOptions);

            var configure = services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).ImplementationFactory(provider);
            var options = new KeyManagementOptions();
            ((IConfigureOptions<KeyManagementOptions>)configure).Configure(options);
            Assert.IsType<KmsXmlEncryptor>(options.XmlEncryptor);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExpectBuilderAdditionsConfig(bool withClient)
        {
            IServiceCollection services = new ServiceCollection();
            A.CallTo(() => builder.Services).Returns(svcCollection);
            A.CallTo(() => svcCollection.GetEnumerator()).ReturnsLazily(() => services.GetEnumerator());
            A.CallTo(() => svcCollection.Add(A<ServiceDescriptor>._))
             .Invokes((ServiceDescriptor sd) => { services.TryAdd(sd); });
            A.CallTo(() => svcCollection.Count).Returns(services.Count);

            // An empty collection seems to be enough to run what is eventually ConfigurationBinder.Bind, since there is no way to mock the options configure call
            // ReSharper disable once CollectionNeverUpdated.Local
            var configChildren = new List<IConfigurationSection>();
            IConfiguration configFake = A.Fake<IConfiguration>(o => o.Strict());
            A.CallTo(() => configFake.GetChildren()).Returns(configChildren);

            // Repeat call to ensure cumulative calls work
            if(withClient)
            {
                builder.ProtectKeysWithAwsKms(client, configFake);
                builder.ProtectKeysWithAwsKms(client, configFake);
            }
            else
            {
                builder.ProtectKeysWithAwsKms(configFake);
                builder.ProtectKeysWithAwsKms(configFake);
            }

            Assert.Equal(withClient ? 1 : 0, services.Count(x => x.ServiceType == typeof(IAmazonKeyManagementService)));

            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)));
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)));

            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)).Lifetime);

            A.CallTo(() => provider.GetService(typeof(IAmazonKeyManagementService))).Returns(client);
            if(withClient)
            {
                Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IAmazonKeyManagementService)).Lifetime);
                Assert.Same(client, services.Single(x => x.ServiceType == typeof(IAmazonKeyManagementService)).ImplementationInstance);
            }

            // Ensure we run equivalent config for the actual configuration object
            var configureObject = services.First(x => x.ServiceType == typeof(IConfigureOptions<KmsXmlEncryptorConfig>)).ImplementationInstance;
            var optionsObject = new KmsXmlEncryptorConfig();
            ((IConfigureOptions<KmsXmlEncryptorConfig>)configureObject).Configure(optionsObject);

            A.CallTo(() => provider.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);
            A.CallTo(() => provider.GetService(typeof(IOptions<KmsXmlEncryptorConfig>))).Returns(snapshot);
            A.CallTo(() => provider.GetService(typeof(IOptions<DataProtectionOptions>))).Returns(dpSnapshot);
            A.CallTo(() => loggerFactory.CreateLogger(typeof(KmsXmlEncryptor).FullName)).Returns(A.Fake<ILogger<KmsXmlEncryptor>>());
            A.CallTo(() => snapshot.Value).Returns(optionsObject);
            var dbOptions = new DataProtectionOptions();
            A.CallTo(() => dpSnapshot.Value).Returns(dbOptions);

            var configure = services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).ImplementationFactory(provider);
            var options = new KeyManagementOptions();
            ((IConfigureOptions<KeyManagementOptions>)configure).Configure(options);
            Assert.IsType<KmsXmlEncryptor>(options.XmlEncryptor);
        }

        [Fact]
        public void ExpectFailureOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() => DataProtectionBuilderExtensions.ProtectKeysWithAwsKms(null, new KmsXmlEncryptorConfig("keyId")));
        }

        [Fact]
        public void ExpectFailureOnNullBuilderWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => DataProtectionBuilderExtensions.ProtectKeysWithAwsKms(null,
                                                                                                             A.Fake<IAmazonKeyManagementService>(),
                                                                                                             new KmsXmlEncryptorConfig("keyId")));
        }

        [Fact]
        public void ExpectFailureOnNullClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.ProtectKeysWithAwsKms(null, new KmsXmlEncryptorConfig("keyId")));
        }

        [Fact]
        public void ExpectFailureOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => builder.ProtectKeysWithAwsKms(null as IConfiguration));
        }

        [Fact]
        public void ExpectFailureOnNullConfigWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.ProtectKeysWithAwsKms(A.Fake<IAmazonKeyManagementService>(), null as IConfiguration));
        }

        [Fact]
        public void ExpectFailureOnNullConfigObject()
        {
            Assert.Throws<ArgumentNullException>(() => builder.ProtectKeysWithAwsKms(null as IKmsXmlEncryptorConfig));
        }

        [Fact]
        public void ExpectFailureOnNullConfigObjectWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.ProtectKeysWithAwsKms(A.Fake<IAmazonKeyManagementService>(),
                                                                                            null as IKmsXmlEncryptorConfig));
        }
    }
}
