# AspNet.DataProtection.Aws

**AWS Alternative**

AWS now actively maintain their own style/[implementation](https://github.com/aws/aws-ssm-data-protection-provider-for-aspnet) of this functionality using SSM.

## Archived Instructions

Amazon Web Services integration for ASP.NET Core data protection.
Server keys can be stored in S3 and/or key material encrypted using KMS using:

- `AspNet.DataProtection.Aws.S3` - S3 encryption key storage
- `AspNet.DataProtection.Aws.Kms` - KMS encryption key protection
[LICENSE.md](LICENSE.md)

This code is open source under the MIT license and not affiliated with Microsoft, Amazon, or any other organisation.

## S3 Persistence

By default, ASP.NET Core Data Protection stores encryption keys locally, causing issues with key mismatches across server farms. S3 can be used to provide XML key file storage instead of a shared
filesystem.

This component deals purely with storage of the XML key files; without Data Protection configured to also encrypt, the key itself is written into each XML file as plaintext
(thus contrasting between encryption options for _storage_ of the file, and whether the key _within_ the file is also encrypted independently). See below for an encryption component
that uses AWS KMS to encrypt the key material within the XML file prior to storage.

Server-side S3 encryption of AES256 is enabled by default. It remains the client's responsibility to ensure access control to the S3 bucket is appropriately configured, as well
as determining whether the various S3 encryption options are sufficient.

[Guidance](https://github.com/aspnet/DataProtection/issues/158) from Microsoft indicates that the repository itself cannot clean up key data as the usage lifetime is not known to
the key management layer. If S3 usage over time is a concern, clients need to trade off key lifetime (and corresponding revocation lifetime) vs S3 storage costs. A suitable approach might
be S3 lifecycle policies to remove ancient key files that could not possibly be in use in the client's deployed scenario. Key files generated by typical `XmlKeyManager` runs are less than 1kB each.

### Configuration

In Startup.cs, specified as part of Data Protection configuration:

```csharp
// Example using IOptions & IConfiguration, where the AWS SDK is injected into Dependency Injection
public void ConfigureServices(IServiceCollection services)
{
    // Configure your AWS SDK however you usually would do so e.g. IAM roles, environment variables
    services.TryAddSingleton<IAmazonS3>(new AmazonS3Client());

    // Assumes a Configuration property set as IConfigurationRoot similar to ASP.NET docs
    services.AddDataProtection()
            .SetApplicationName("my-application-name") // Not required by S3 storage but a requirement for server farms
            .PersistKeysToAwsS3(Configuration.GetSection("myS3XmlStorageConfiguration"));
            // You may wish to configure internal encryption of the key material via a ProtectKeysWithX config entry, or use S3 encryption
}

// Example using direct options & SDK instantiation
public void ConfigureServices(IServiceCollection services)
{
    services.AddDataProtection()
            .PersistKeysToAwsS3(new AmazonS3Client(), new S3XmlRepositoryConfig("my-bucket-name")
            // Configuration has defaults; all below are OPTIONAL
            {
                // How many concurrent connections will be made to S3 to retrieve key data
                MaxS3QueryConcurrency = 10,
                // Custom prefix in the S3 bucket enabling use of folders
                KeyPrefix = "MyKeys/",
                // Customise storage class for key storage
                StorageClass = S3StorageClass.Standard,
                // Customise encryption options (these can be mutually exclusive - don't just copy & paste!)
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
                ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
                ServerSideEncryptionCustomerProvidedKey = "MyBase64Key",
                ServerSideEncryptionCustomerProvidedKeyMD5 = "MD5OfMyBase64Key",
                ServerSideEncryptionKeyManagementServiceKeyId = "AwsKeyManagementServiceId",
                // Compress stored XML before write to S3
                ClientSideCompression = true,
                // Validate downloads
                ValidateETag = false,
                ValidateMd5Metadata = true
            });
}
```
S3 bucket name _must_ be specified. All other options have standard server-side secure defaults. If the `IAmazonS3` interface is discoverable
via `IServiceCollection`, the argument of `AmazonS3Client` can be omitted.

### Required Permissions

If you're using Infrastructure as Code, like CloudFormation, or Terraform, you will need the exact permissions for the bucket. These are needed:

- `s3:GetObject`
- `s3:ListBucket`
- `s3:PutObject`

## KMS Cryptography

Default options for ASP.NET data encryption are bound to certificates or Windows-specific DPAPI constructs. AWS Key Management Service
keys can be used instead to provide a consistent master key for protecting the temporary server key material itself while stored within the XML files.

Please note that `IServiceProvider`/`IServiceCollection` Dependency Injection is _required_ for this to operate correctly, due to the
Data Protection key manager needing to locate & create the appropriate `IXmlDecryptor` on demand.

It remains the client's responsibility to correctly configure access control to the chosen KMS key, and to determine whether their precise
scenario requires grants or particular encryption contexts.

### Configuration

In Startup.cs, specified as part of Data Protection configuration:

```csharp
// Example using IOptions & IConfiguration, where the AWS SDK is injected into Dependency Injection
public void ConfigureServices(IServiceCollection services)
{
    // Configure your AWS SDK however you usually would do so e.g. IAM roles, environment variables
    services.TryAddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient());

    // Assumes a Configuration property set as IConfigurationRoot similar to ASP.NET docs
    services.AddDataProtection()
            .SetApplicationName("my-application-name") // If populated, this will be used as part of the KMS encryption context to add security
            // You will need to specify some suitable persistence of the key material via a PersistKeysToX entry
            .ProtectKeysWithAwsKms(Configuration.GetSection("mykmsXmlEncryptionConfiguration"));
}

// Example using direct options & SDK instantiation
public void ConfigureServices(IServiceCollection services)
{
    var kmsConfig = new KmsXmlEncryptorConfig("alias/MyKmsAlias");
    // Configuration has default contexts added; below are optional if using grants or additional contexts
    kmsConfig.EncryptionContext.Add("my-custom-context", "my-custom-value");
    kmsConfig.GrantTokens.Add("my-grant-token");
    // Include the application discriminator as part of the KMS encryption context to aid application isolation
    kmsConfig.DiscriminatorAsContext = true;
    // Encryption contexts can be viewed in logs; if the discriminator is sensitive, hash before use as a context value
    kmsConfig.HashDiscriminatorContext = true;
147680
    services.AddDataProtection()
            .SetApplicationName("my-application-name") // If populated & DiscriminatorAsContext = true, this will be used as part of the KMS encryption context
            .ProtectKeysWithAwsKms(new AmazonKeyManagementServiceClient(), kmsConfig);
}
```
KMS key ID _must_ be specified. If the `IAmazonKeyManagementService` interface is discoverable via Dependency Injection in `IServiceCollection`, the constructor argument of `AmazonKeyManagementServiceClient` can be omitted.

_Migration Note:_ The `1.0` release of `AspNet.DataProtection.Aws.Kms` had `KmsXmlEncryptorConfig` take the application name as an argument, which was then used
to populate an encryption context. The Data Protection application discriminator is now used to provide this value as it fulfils a similar function - that of identifying and
allowing/preventing cross-talk between applications.

To ensure correct operation against existing data encrypted with `1.0`, include `SetApplicationName`, set `DiscriminatorAsContext` to `true` and
`HashDiscriminatorContext` to `false` when setting up Data Protection for matching functionality. If these values need to differ, the above can instead be
created as a custom context with key of `KmsConstants.ApplicationEncryptionContextKey`.

## Building

Prerequisites for building & testing:

- NET SDK 6+

Integration tests require AWS access, or modifying to access your own copy of AWS resources.
