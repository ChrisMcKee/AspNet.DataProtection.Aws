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
        var containerName = $"aspnet-dpa-aws-localstack-{Guid.NewGuid():N}";
        _container = new LocalStackBuilder()
                     .WithImage("localstack/localstack:4")
                     .WithName(containerName)
                     .WithPortBinding(4566, true) // Use dynamic port allocation
                     .WithCleanUp(true)
                     .WithEnvironment("SERVICES", "s3,kms")
                     .WithEnvironment("DEBUG", "1")
                     .WithWaitStrategy(Wait.ForUnixContainer()
                                           .UntilInternalTcpPortIsAvailable(4566)
                                           .AddCustomWaitStrategy(new LocalstackContainerHealthCheck())
                                      )
                     .WithBindMount(ToAbsolute("./localstack/aws-seed-data"), "/etc/localstack/init/ready.d", AccessMode.ReadOnly)
                     .WithBindMount(ToAbsolute("./localstack/aws-seed-data/scripts"), "/scripts", AccessMode.ReadOnly)
                     .Build();
    }

    public string? ConnectionString { get; private set; }

    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await _container.StartAsync(cts.Token);
        ConnectionString = _container.GetConnectionString();

        // Wait for LocalStack to be fully ready and initialization scripts to complete
        await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
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
        // Use TestContainers LocalStack instance with dummy credentials
        kmsClient = new AmazonKeyManagementServiceClient("test", "test",
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

        // Change the application discriminator to a different value
        dpOptions.ApplicationDiscriminator = "wrong";

        // This should throw an InvalidCiphertextException because the context is different
        await Assert.ThrowsAsync<InvalidCiphertextException>(
                                                             async () => await decryptor.DecryptAsync(encrypted.EncryptedElement, CancellationToken.None));
    }
}
