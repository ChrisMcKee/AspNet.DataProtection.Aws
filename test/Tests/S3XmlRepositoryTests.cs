﻿// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using AspNetCore.DataProtection.Aws.S3;
using AspNetCore.DataProtection.Aws.S3.Internals;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AspNetCore.DataProtection.Aws.Tests
{
    public sealed class S3XmlRespositoryTests : IDisposable
    {
        private readonly S3XmlRepository xmlRepository;
        private readonly MockRepository repository;
        private readonly Mock<IAmazonS3> s3Client;
        private readonly Mock<IOptions<S3XmlRepositoryConfig>> config;
        private readonly Mock<IMockingWrapper> mockingWrapper;
        private const string ElementName = "name";
        private const string ElementContent = "test";
        private const string Bucket = "bucket";
        private const string Prefix = "prefix";
        private const string AesKey = "x+AmYqxeD//Ky4vt0HmXxSVGll7TgEkJK6iTPGqFJbk=";

        public S3XmlRespositoryTests()
        {
            repository = new MockRepository(MockBehavior.Strict);
            s3Client = repository.Create<IAmazonS3>();
            config = repository.Create<IOptions<S3XmlRepositoryConfig>>();
            mockingWrapper = repository.Create<IMockingWrapper>();
            xmlRepository = new S3XmlRepository(s3Client.Object, config.Object, null, mockingWrapper.Object);
        }

        public void Dispose()
        {
            repository.VerifyAll();
        }

        [Fact]
        public void ExpectAlternativeConstructor()
        {
            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket };
            config.Setup(x => x.Value).Returns(configObject);

            var altRepo = new S3XmlRepository(s3Client.Object, config.Object);

            Assert.Same(configObject, altRepo.Config);
        }

        [Fact]
        public void ExpectValidationOfConfigToThrow()
        {
            var configObject = new S3XmlRepositoryConfig();
            config.Setup(x => x.Value).Returns(configObject);

            var altRepo = new S3XmlRepository(s3Client.Object, config.Object);

            Assert.Throws<ArgumentException>(() => altRepo.ValidateConfig());
        }

        [Fact]
        public void ExpectStoreToSucceed()
        {
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix, ClientSideCompression = false };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            GetObjectMetadataResponse headResponse = null;
            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.AES256, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Null(pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.Standard, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       headResponse = new GetObjectMetadataResponse();
                                                                       headResponse.Metadata[S3XmlRepository.Md5ActualMetadataHeader] = metadataHeader;
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(pr.InputStream);
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(() => headResponse)
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            xmlRepository.StoreElement(myXml, myTestName);
        }

        [Fact]
        public void ExpectMetadataMismatchToThrow()
        {
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix, ClientSideCompression = false };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.AES256, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Null(pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.Standard, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(pr.InputStream);
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(new GetObjectMetadataResponse())
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            Assert.Throws<AggregateException>(() => xmlRepository.StoreElement(myXml, myTestName));
        }

        [Fact]
        public void ExpectKmsStoreToSucceed()
        {
            var keyId = "keyId";
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig
            {
                Bucket = Bucket,
                KeyPrefix = Prefix,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS,
                ServerSideEncryptionKeyManagementServiceKeyId = keyId,
                ClientSideCompression = false
            };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            GetObjectMetadataResponse headResponse = null;
            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.AWSKMS, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Equal(keyId, pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.Standard, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       headResponse = new GetObjectMetadataResponse();
                                                                       headResponse.Metadata[S3XmlRepository.Md5ActualMetadataHeader] = metadataHeader;
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(pr.InputStream);
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(() => headResponse)
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            xmlRepository.StoreElement(myXml, myTestName);
        }

        [Fact]
        public void ExpectCustomStoreToSucceed()
        {
            var md5 = "md5";
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig
            {
                Bucket = Bucket,
                KeyPrefix = Prefix,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.None,
                ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
                ServerSideEncryptionCustomerProvidedKey = AesKey,
                ServerSideEncryptionCustomerProvidedKeyMd5 = md5,
                ClientSideCompression = false
            };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            GetObjectMetadataResponse headResponse = null;
            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.None, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.AES256, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Equal(AesKey, pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Equal(md5, pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Null(pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.Standard, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       headResponse = new GetObjectMetadataResponse();
                                                                       headResponse.Metadata[S3XmlRepository.Md5ActualMetadataHeader] = metadataHeader;
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(pr.InputStream);
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(() => headResponse)
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.AES256, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Equal(AesKey, pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Equal(md5, pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            xmlRepository.StoreElement(myXml, myTestName);
        }

        [Fact]
        public void ExpectVariedStorageClassToSucceed()
        {
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix, StorageClass = S3StorageClass.ReducedRedundancy, ClientSideCompression = false };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            GetObjectMetadataResponse headResponse = null;
            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.AES256, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Null(pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.ReducedRedundancy, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       headResponse = new GetObjectMetadataResponse();
                                                                       headResponse.Metadata[S3XmlRepository.Md5ActualMetadataHeader] = metadataHeader;
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(pr.InputStream);
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(() => headResponse)
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            xmlRepository.StoreElement(myXml, myTestName);
        }

        [Fact]
        public void ExpectCompressedStoreToSucceed()
        {
            var myXml = new XElement(ElementName, ElementContent);
            var myTestName = "friendly";

            // Response isn't queried, so can be default arguments
            var response = new PutObjectResponse();

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            var guid = new Guid("03ffb238-1f6b-4647-963a-5ed60e83c74e");
            mockingWrapper.Setup(x => x.GetNewGuid()).Returns(guid);

            GetObjectMetadataResponse headResponse = null;
            s3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), CancellationToken.None))
                    .ReturnsAsync(response)
                    .Callback<PutObjectRequest, CancellationToken>((pr, ct) =>
                                                                   {
                                                                       Assert.Equal(Bucket, pr.BucketName);
                                                                       Assert.Equal(ServerSideEncryptionMethod.AES256, pr.ServerSideEncryptionMethod);
                                                                       Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                       Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       Assert.Null(pr.ServerSideEncryptionKeyManagementServiceKeyId);
                                                                       Assert.Equal(S3StorageClass.Standard, pr.StorageClass);
                                                                       Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                       Assert.Equal("gzip", pr.Headers.ContentEncoding);
                                                                       Assert.Contains(S3XmlRepository.FriendlyNameActualMetadataHeader, pr.Metadata.Keys);
                                                                       Assert.Equal(myTestName, pr.Metadata[S3XmlRepository.FriendlyNameActualMetadataHeader]);
                                                                       Assert.Contains(S3XmlRepository.Md5ActualMetadataHeader, pr.Metadata.Keys);
                                                                       var metadataHeader = pr.Metadata[S3XmlRepository.Md5ActualMetadataHeader];
                                                                       headResponse = new GetObjectMetadataResponse();
                                                                       headResponse.Metadata[S3XmlRepository.Md5ActualMetadataHeader] = metadataHeader;
                                                                       Assert.Equal(Convert.FromBase64String(pr.MD5Digest), StringToByteArray(metadataHeader));

                                                                       var body = XElement.Load(new GZipStream(pr.InputStream, CompressionMode.Decompress));
                                                                       Assert.True(XNode.DeepEquals(myXml, body));
                                                                   });

            s3Client.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), CancellationToken.None))
                    .ReturnsAsync(() => headResponse)
                    .Callback<GetObjectMetadataRequest, CancellationToken>((pr, ct) =>
                                                                           {
                                                                               Assert.Equal(Bucket, pr.BucketName);
                                                                               Assert.Equal(ServerSideEncryptionCustomerMethod.None, pr.ServerSideEncryptionCustomerMethod);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKey);
                                                                               Assert.Null(pr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                               Assert.Equal(Prefix + guid + ".xml", pr.Key);
                                                                           });

            xmlRepository.StoreElement(myXml, myTestName);
        }

        [Fact]
        public void ExpectEmptyQueryToSucceed()
        {
            var listResponse = new ListObjectsV2Response 
            { 
                Name = Bucket, 
                Prefix = Prefix,
                S3Objects = new List<S3Object>() // Initialize empty list to avoid null reference
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

            Assert.Empty(list);
        }

        [Theory]
        [InlineData("garbage", null, false, true)]
        [InlineData(null, "garbage", true, false)]
        [InlineData(null, null, false, false)]
        [InlineData(null, null, false, true)]
        [InlineData(null, null, true, false)]
        [InlineData(null, null, true, true)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", null, true, false)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", null, false, false)]
        [InlineData(null, "51df532e0190642dfbf0e15105fd7827", false, true)]
        [InlineData(null, "51df532e0190642dfbf0e15105fd7827", false, false)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", false, false)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", true, false)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", false, true)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", true, true)]
        [InlineData("\"AB-51df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", true, true)] // Ensure fallback to metadata
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "61df532e0190642dfbf0e15105fd7827", true, true)] // Trust the ETag more
        public void ExpectSingleQueryToSucceed(string etag, string md5Header, bool validateETag, bool validateMetadata)
        {
            var key = "key";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix, ValidateETag = validateETag, ValidateMd5Metadata = validateMetadata };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                myXml.Save(returnedStream);
                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse { BucketName = Bucket, ETag = etag, Key = key, ResponseStream = returnedStream };
                if(md5Header != null)
                {
                    getResponse.Metadata.Add(S3XmlRepository.Md5Metadata, md5Header);
                }

                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.None, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Single(list);

                Assert.True(XNode.DeepEquals(myXml, list.First()));
            }
        }

        [Theory]
        [InlineData("\"61df532e0190642dfbf0e15105fd7827\"", null, true, false)]
        [InlineData(null, "61df532e0190642dfbf0e15105fd7827", false, true)]
        [InlineData("\"61df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", true, false)]
        [InlineData("\"51df532e0190642dfbf0e15105fd7827\"", "61df532e0190642dfbf0e15105fd7827", false, true)]
        [InlineData("\"61df532e0190642dfbf0e15105fd7827\"", "51df532e0190642dfbf0e15105fd7827", true, true)]
        [InlineData(null, "61df532e0190642dfbf0e15105fd7827", true, true)]
        [InlineData("\"61df532e0190642dfbf0e15105fd7827\"", null, true, true)]
        [InlineData("\"6-df532e0190642dfbf0e15105fd7827\"", "61df532e0190642dfbf0e15105fd7827", true, true)]
        [InlineData("\"251df532e0190642dfbf0e15105fd7827\"", "61df532e0190642dfbf0e15105fd7827", true, true)]
        public void ExpectCorruptQueryToThrow(string etag, string md5Header, bool validateETag, bool validateMetadata)
        {
            var key = "key";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix, ValidateETag = validateETag, ValidateMd5Metadata = validateMetadata };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                myXml.Save(returnedStream);
                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse
                {
                    BucketName = Bucket,
                    ETag = etag,
                    Key = key,
                    ResponseStream = returnedStream,
                    ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.None
                };
                if(md5Header != null)
                {
                    getResponse.Metadata.Add(S3XmlRepository.Md5Metadata, md5Header);
                }

                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.None, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                Assert.Throws<AggregateException>(() => xmlRepository.GetAllElements());
            }
        }

        [Fact]
        public void ExpectFolderIgnored()
        {
            var key = "folder/";
            var etag = "etag";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                myXml.Save(returnedStream);
                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse { BucketName = Bucket, ETag = etag, Key = key, ResponseStream = returnedStream };
                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.None, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Empty(list);
            }
        }

        [Fact]
        public void ExpectSingleUncompressedCompatibleQueryToSucceed()
        {
            var key = "key";
            var etag = "etag";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                myXml.Save(returnedStream);
                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse { BucketName = Bucket, ETag = etag, Key = key, ResponseStream = returnedStream };
                // No Content-Encoding specified
                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.None, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Single(list);

                Assert.True(XNode.DeepEquals(myXml, list.First()));
            }
        }

        [Fact]
        public void ExpectSingleCompressedCompatibleQueryToSucceed()
        {
            var key = "key";
            var etag = "etag";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                using(var inputStream = new MemoryStream())
                {
                    using(var gZippedstream = new GZipStream(inputStream, CompressionMode.Compress))
                    {
                        myXml.Save(gZippedstream);
                    }

                    byte[] inputArray = inputStream.ToArray();
                    returnedStream.Write(inputArray, 0, inputArray.Length);
                }

                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse { BucketName = Bucket, ETag = etag, Key = key, ResponseStream = returnedStream };
                getResponse.Headers.ContentEncoding = "gzip";
                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.None, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Null(gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Single(list);

                Assert.True(XNode.DeepEquals(myXml, list.First()));
            }
        }

        [Fact]
        public void ExpectCustomSingleQueryToSucceed()
        {
            var md5 = "md5";
            var key = "key";
            var etag = "etag";

            var listResponse = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key, ETag = etag } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig
            {
                Bucket = Bucket,
                KeyPrefix = Prefix,
                ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
                ServerSideEncryptionCustomerProvidedKey = AesKey,
                ServerSideEncryptionCustomerProvidedKeyMd5 = md5
            };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), CancellationToken.None))
                    .ReturnsAsync(listResponse)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Null(lr.ContinuationToken);
                                                                       });

            using(var returnedStream = new MemoryStream())
            {
                var myXml = new XElement(ElementName, ElementContent);
                myXml.Save(returnedStream);
                returnedStream.Seek(0, SeekOrigin.Begin);

                var getResponse = new GetObjectResponse { BucketName = Bucket, ETag = etag, Key = key, ResponseStream = returnedStream };
                s3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), CancellationToken.None))
                        .ReturnsAsync(getResponse)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key, gr.Key);
                                                                           Assert.Equal(ServerSideEncryptionCustomerMethod.AES256, gr.ServerSideEncryptionCustomerMethod);
                                                                           Assert.Equal(AesKey, gr.ServerSideEncryptionCustomerProvidedKey);
                                                                           Assert.Equal(md5, gr.ServerSideEncryptionCustomerProvidedKeyMD5);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Single(list);

                Assert.True(XNode.DeepEquals(myXml, list.First()));
            }
        }

        [Fact]
        public void ExpectMultiQueryToSucceed()
        {
            var key1 = "key1";
            var etag1 = "etag1";
            var key2 = "key2";
            var etag2 = "etag2";
            var nextToken = "next";

            var listResponse1 = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key1, ETag = etag1 } },
                IsTruncated = true,
                NextContinuationToken = nextToken
            };
            var listResponse2 = new ListObjectsV2Response
            {
                Name = Bucket,
                Prefix = Prefix,
                S3Objects = new List<S3Object> { new S3Object { Key = key2, ETag = etag2 } },
                IsTruncated = false
            };

            var configObject = new S3XmlRepositoryConfig { Bucket = Bucket, KeyPrefix = Prefix };

            config.Setup(x => x.Value).Returns(configObject);

            s3Client.Setup(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(lr => lr.ContinuationToken == null), CancellationToken.None))
                    .ReturnsAsync(listResponse1)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                       });
            s3Client.Setup(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(lr => lr.ContinuationToken != null), CancellationToken.None))
                    .ReturnsAsync(listResponse2)
                    .Callback<ListObjectsV2Request, CancellationToken>((lr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, lr.BucketName);
                                                                           Assert.Equal(Prefix, lr.Prefix);
                                                                           Assert.Equal(nextToken, lr.ContinuationToken);
                                                                       });

            using(var returnedStream1 = new MemoryStream())
            using(var returnedStream2 = new MemoryStream())
            {
                var myXml1 = new XElement(ElementName, ElementContent + "1");
                var myXml2 = new XElement(ElementName, ElementContent + "2");
                myXml1.Save(returnedStream1);
                returnedStream1.Seek(0, SeekOrigin.Begin);
                myXml2.Save(returnedStream2);
                returnedStream2.Seek(0, SeekOrigin.Begin);

                var getResponse1 = new GetObjectResponse { BucketName = Bucket, ETag = etag1, Key = key1, ResponseStream = returnedStream1 };
                var getResponse2 = new GetObjectResponse { BucketName = Bucket, ETag = etag2, Key = key2, ResponseStream = returnedStream2 };
                s3Client.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(gr => gr.Key == key1), CancellationToken.None))
                        .ReturnsAsync(getResponse1)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key1, gr.Key);
                                                                       });
                s3Client.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(gr => gr.Key == key2), CancellationToken.None))
                        .ReturnsAsync(getResponse2)
                        .Callback<GetObjectRequest, CancellationToken>((gr, ct) =>
                                                                       {
                                                                           Assert.Equal(Bucket, gr.BucketName);
                                                                           Assert.Equal(key2, gr.Key);
                                                                       });

                IReadOnlyCollection<XElement> list = xmlRepository.GetAllElements();

                Assert.Equal(2, list.Count);

                Assert.True(XNode.DeepEquals(myXml1, list.First()));
                Assert.True(XNode.DeepEquals(myXml2, list.Last()));
            }
        }

        public static byte[] StringToByteArray(string hex)
        {
            int numberChars = hex.Length;
            byte[] bytes = new byte[numberChars / 2];
            for(int i = 0; i < numberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }
}
