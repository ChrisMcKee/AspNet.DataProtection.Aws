// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.S3;
using AspNetCore.DataProtection.Aws.S3;
using AspNetCore.DataProtection.Aws.S3.Internals;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FakeItEasy;
using Xunit;
using DataProtectionBuilderExtensions = AspNetCore.DataProtection.Aws.S3.DataProtectionBuilderExtensions;

namespace AspNetCore.DataProtection.Aws.Tests
{
    public class S3DataProtectionBuilderExtensionsTests
    {
        private readonly IDataProtectionBuilder builder;
        private readonly IServiceCollection svcCollection;
        private readonly IAmazonS3 client;
        private readonly IServiceProvider provider;
        private readonly ILoggerFactory loggerFactory;
        private readonly IOptions<S3XmlRepositoryConfig> snapshot;

        public S3DataProtectionBuilderExtensionsTests()
        {
            builder = A.Fake<IDataProtectionBuilder>(o => o.Strict());
            client = A.Fake<IAmazonS3>(o => o.Strict());
            svcCollection = A.Fake<IServiceCollection>(o => o.Strict());
            provider = A.Fake<IServiceProvider>(o => o.Strict());
            loggerFactory = A.Fake<ILoggerFactory>(o => o.Strict());
            snapshot = A.Fake<IOptions<S3XmlRepositoryConfig>>(o => o.Strict());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExpectBuilderAdditionsRaw(bool withClient)
        {
            IServiceCollection services = new ServiceCollection();
            A.CallTo(() => builder.Services).Returns(svcCollection);
            A.CallTo(() => svcCollection.GetEnumerator()).ReturnsLazily(() => services.GetEnumerator());
            A.CallTo(() => svcCollection.Add(A<ServiceDescriptor>._))
             .Invokes((ServiceDescriptor sd) => { services.TryAdd(sd); });
            A.CallTo(() => svcCollection.Count).Returns(services.Count);

            var config = new S3XmlRepositoryConfig("bucket");

            // Repeat call to ensure cumulative calls work
            if(withClient)
            {
                builder.PersistKeysToAwsS3(client, config);
                builder.PersistKeysToAwsS3(client, config);
            }
            else
            {
                builder.PersistKeysToAwsS3(config);
                builder.PersistKeysToAwsS3(config);
                A.CallTo(() => provider.GetService(typeof(IAmazonS3))).Returns(client);
            }

            Assert.Equal(1, services.Distinct().Count(x => x.ServiceType == typeof(IMockingWrapper)));

            // Behaviour of TryAdd stops duplicates
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)));
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)));

            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IMockingWrapper)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.First(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)).Lifetime);

            // Ensure we run equivalent config for the actual configuration object
            var configureObject = services.First(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)).ImplementationInstance;
            var optionsObject = new S3XmlRepositoryConfig();
            ((IConfigureOptions<S3XmlRepositoryConfig>)configureObject).Configure(optionsObject);

            A.CallTo(() => provider.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);
            A.CallTo(() => provider.GetService(typeof(IOptions<S3XmlRepositoryConfig>))).Returns(snapshot);
            A.CallTo(() => loggerFactory.CreateLogger(typeof(S3XmlRepository).FullName)).Returns(A.Fake<ILogger<S3XmlRepository>>());
            A.CallTo(() => snapshot.Value).Returns(optionsObject);

            var configure = services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).ImplementationFactory(provider);
            var options = new KeyManagementOptions();
            ((IConfigureOptions<KeyManagementOptions>)configure).Configure(options);
            Assert.IsType<S3XmlRepository>(options.XmlRepository);
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
                builder.PersistKeysToAwsS3(client, configFake);
                builder.PersistKeysToAwsS3(client, configFake);
            }
            else
            {
                builder.PersistKeysToAwsS3(configFake);
                builder.PersistKeysToAwsS3(configFake);
                A.CallTo(() => provider.GetService(typeof(IAmazonS3))).Returns(client);
            }

            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IMockingWrapper)));

            // Behaviour of TryAdd stops duplicates
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)));
            Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)));

            Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IMockingWrapper)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.First(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)).Lifetime);

            // Ensure we run equivalent config for the actual configuration object
            var configureObject = services.First(x => x.ServiceType == typeof(IConfigureOptions<S3XmlRepositoryConfig>)).ImplementationInstance;
            var optionsObject = new S3XmlRepositoryConfig();
            ((IConfigureOptions<S3XmlRepositoryConfig>)configureObject).Configure(optionsObject);

            A.CallTo(() => provider.GetService(typeof(ILoggerFactory))).Returns(loggerFactory);
            A.CallTo(() => provider.GetService(typeof(IOptions<S3XmlRepositoryConfig>))).Returns(snapshot);
            A.CallTo(() => loggerFactory.CreateLogger(typeof(S3XmlRepository).FullName)).Returns(A.Fake<ILogger<S3XmlRepository>>());
            A.CallTo(() => snapshot.Value).Returns(optionsObject);

            var configure = services.First(x => x.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)).ImplementationFactory(provider);
            var options = new KeyManagementOptions();
            ((IConfigureOptions<KeyManagementOptions>)configure).Configure(options);
            Assert.IsType<S3XmlRepository>(options.XmlRepository);
        }

        [Fact]
        public void ExpectFailureOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() => DataProtectionBuilderExtensions.PersistKeysToAwsS3(null, new S3XmlRepositoryConfig("bucketName")));
        }

        [Fact]
        public void ExpectFailureOnNullBuilderWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => DataProtectionBuilderExtensions.PersistKeysToAwsS3(null,
                                                                                                          A.Fake<IAmazonS3>(),
                                                                                                          new S3XmlRepositoryConfig("bucket")));
        }

        [Fact]
        public void ExpectFailureOnNullClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.PersistKeysToAwsS3(null, new S3XmlRepositoryConfig("bucket")));
        }

        [Fact]
        public void ExpectFailureOnNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => builder.PersistKeysToAwsS3(null as IConfiguration));
        }

        [Fact]
        public void ExpectFailureOnNullConfigWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.PersistKeysToAwsS3(A.Fake<IAmazonS3>(), null as IConfiguration));
        }

        [Fact]
        public void ExpectFailureOnNullConfigObject()
        {
            Assert.Throws<ArgumentNullException>(() => builder.PersistKeysToAwsS3(null as IS3XmlRepositoryConfig));
        }

        [Fact]
        public void ExpectFailureOnNullConfigObjectWithClient()
        {
            Assert.Throws<ArgumentNullException>(() => builder.PersistKeysToAwsS3(A.Fake<IAmazonS3>(), null as IS3XmlRepositoryConfig));
        }
    }
}
