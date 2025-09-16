// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using AspNetCore.DataProtection.Aws.Kms;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.LocalStack;
using Xunit;

namespace AspNetCore.DataProtection.Aws.IntegrationTests;

[CollectionDefinition(nameof(LocalStackTestContainerCollection))]
public class LocalStackTestContainerCollection :
    ICollectionFixture<LocalStackFixture>
{
}

public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _container;

    public LocalStackFixture()
    {
        // var localStackPort = new Random().Next(4000, 5000);
        _container = new LocalStackBuilder()
                     .WithName("aspnet.dpa.aws.s3")
                     .WithPortBinding(45666,4566)
                     .WithCleanUp(false)
                     // .WithPortBinding(4566, true)
                     .WithWaitStrategy(Wait.ForUnixContainer()
                                           //.UntilPortIsAvailable(4566)
                                           //.AddCustomWaitStrategy(new LocalstackContainerHealthCheck($"http://localhost:{localStackPort}"))
                                      )
                     // .WithBindMount(ToAbsolute("./localstack/aws-seed-data"), "/etc/localstack/init/ready.d", AccessMode.ReadOnly)
                     // .WithBindMount(ToAbsolute("./localstack/aws-seed-data/scripts"), "/scripts", AccessMode.ReadOnly)
                     .Build();
    }

    public string? ConnectionString { get; private set; }

    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await _container.StartAsync(cts.Token);
        ConnectionString = _container.GetConnectionString();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if(_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private static string ToAbsolute(string path) => Path.GetFullPath(path);
}

[Collection(nameof(LocalStackTestContainerCollection))]
public class KmsIntegrationTests
{
    private readonly KmsXmlEncryptor encryptor;
    private readonly KmsXmlDecryptor decryptor;
    private readonly IAmazonKeyManagementService kmsClient;
    private readonly IServiceProvider svcProvider;
    internal const string ApplicationName = "dpa-test-app";
    private const string ElementName = "name";
    private const string ElementContent = "test";
    // Expectation that whatever key is in use has this alias
    internal const string KmsTestingKey = "alias/KmsIntegrationTesting";
    private readonly DataProtectionOptions dpOptions;

    public KmsIntegrationTests(LocalStackFixture containerInstance)
    {
        // Expectation that local SDK has been configured correctly, whether via VS Tools or user config files
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "xxx");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "xxx");
        kmsClient = new AmazonKeyManagementServiceClient(
                                                         new AmazonKeyManagementServiceConfig()
                                                         {
                                                             UseHttp = true,
                                                             ServiceURL = containerInstance.ConnectionString,
                                                         });
        var encryptConfig = new KmsXmlEncryptorConfig(KmsTestingKey);
        dpOptions = new DataProtectionOptions { ApplicationDiscriminator = ApplicationName };
        var encryptSnapshot = new DirectOptions<KmsXmlEncryptorConfig>(encryptConfig);
        var dpSnapshot = new DirectOptions<DataProtectionOptions>(dpOptions);

        var svcCollection = new ServiceCollection();
        svcCollection.AddSingleton<IOptions<KmsXmlEncryptorConfig>>(sp => encryptSnapshot);
        svcCollection.AddSingleton<IOptions<DataProtectionOptions>>(sp => dpSnapshot);
        svcCollection.AddSingleton(sp => kmsClient);
        svcProvider = svcCollection.BuildServiceProvider();

        encryptor = new KmsXmlEncryptor(kmsClient, encryptSnapshot, dpSnapshot);
        decryptor = new KmsXmlDecryptor(svcProvider);
    }

    [Fact]
    public async Task ExpectRoundTripToSucceed()
    {
        var myXml = new XElement(ElementName, ElementContent);

        var encrypted = await encryptor.EncryptAsync(myXml, CancellationToken.None);

        var decrypted = await decryptor.DecryptAsync(encrypted.EncryptedElement, CancellationToken.None);

        Assert.True(XNode.DeepEquals(myXml, decrypted));
    }

    [Fact]
    public async Task ExpectDifferentContextsToFail()
    {
        var myXml = new XElement(ElementName, ElementContent);

        var encrypted = await encryptor.EncryptAsync(myXml, CancellationToken.None);

        dpOptions.ApplicationDiscriminator = "wrong";

        var result = await decryptor.DecryptAsync(encrypted.EncryptedElement, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidCiphertextException>(
                                                             async () => await decryptor.DecryptAsync(encrypted.EncryptedElement, CancellationToken.None));
    }
}
