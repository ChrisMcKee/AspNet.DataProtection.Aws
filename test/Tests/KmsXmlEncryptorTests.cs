// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using AspNetCore.DataProtection.Aws.Kms;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using FakeItEasy;
using Xunit;

namespace AspNetCore.DataProtection.Aws.Tests
{
    public class KmsXmlEncryptorTests
    {
        private readonly KmsXmlEncryptor encryptor;
        private readonly IAmazonKeyManagementService kmsClient;
        private readonly IOptions<KmsXmlEncryptorConfig> encryptConfig;
        private readonly IOptions<DataProtectionOptions> dpOptions;
        private const string KeyId = "keyId";
        private const string ElementName = "name";
        private readonly Dictionary<string, string> encryptionContext = new Dictionary<string, string>();
        private readonly List<string> grantTokens = new List<string>();

        public KmsXmlEncryptorTests()
        {
            kmsClient = A.Fake<IAmazonKeyManagementService>(o => o.Strict());
            encryptConfig = A.Fake<IOptions<KmsXmlEncryptorConfig>>(o => o.Strict());
            dpOptions = A.Fake<IOptions<DataProtectionOptions>>(o => o.Strict());

            encryptor = new KmsXmlEncryptor(kmsClient, encryptConfig, dpOptions);
        }

        [Fact]
        public void ExpectValidationOfConfigToThrow()
        {
            var configObject = new KmsXmlEncryptorConfig();
            A.CallTo(() => encryptConfig.Value).Returns(configObject);

            var altRepo = new KmsXmlEncryptor(kmsClient, encryptConfig, dpOptions);

            Assert.Throws<ArgumentException>(() => altRepo.ValidateConfig());
        }

        [Theory]
        [InlineData(true, false, null, null)]
        [InlineData(true, false, "appId", "appId")]
        [InlineData(true, true, "appId", "MQbS8HXC/tbfUWH41GswKXp1I8W5LxnEQM/w+rgusJY=")]
        [InlineData(true, true, "bob", "gbY32PzSxtpjWeaWMROhFw3nleS3JbhNHgtM/Z7FjOk=")]
        [InlineData(false, false, "appId", null)]
        [InlineData(false, true, "appId", null)]
        public void ExpectEncryptToSucceed(bool useAppId, bool hashAppId, string appId, string expectedAppId)
        {
            var myInputXml = new XElement(ElementName, "input");
            byte[] myEncryptedData = Encoding.UTF8.GetBytes("encrypted");
            var myBase64EncryptedData = Convert.ToBase64String(myEncryptedData);

            using(var encryptedResponseStream = new MemoryStream())
            {
                encryptedResponseStream.Write(myEncryptedData, 0, myEncryptedData.Length);
                encryptedResponseStream.Seek(0, SeekOrigin.Begin);

                var encryptResponse = new EncryptResponse { KeyId = KeyId, CiphertextBlob = encryptedResponseStream };

                var actualConfig = new KmsXmlEncryptorConfig
                {
                    EncryptionContext = encryptionContext,
                    GrantTokens = grantTokens,
                    KeyId = KeyId,
                    DiscriminatorAsContext = useAppId,
                    HashDiscriminatorContext = hashAppId
                };

                var actualOptions = new DataProtectionOptions { ApplicationDiscriminator = appId };

                A.CallTo(() => encryptConfig.Value).Returns(actualConfig);
                A.CallTo(() => dpOptions.Value).Returns(actualOptions);

                A.CallTo(() => kmsClient.EncryptAsync(A<EncryptRequest>._, CancellationToken.None))
                 .Invokes((EncryptRequest er, CancellationToken ct) =>
                                                              {
                                                                  if(appId != null && useAppId)
                                                                  {
                                                                      Assert.Contains(KmsConstants.ApplicationEncryptionContextKey, er.EncryptionContext.Keys);
                                                                      Assert.Equal(expectedAppId, er.EncryptionContext[KmsConstants.ApplicationEncryptionContextKey]);
                                                                  }
                                                                  else
                                                                  {
                                                                      Assert.Same(encryptionContext, er.EncryptionContext);
                                                                  }

                                                                  Assert.Same(grantTokens, er.GrantTokens);

                                                                  var body = XElement.Load(er.Plaintext);
                                                                  Assert.True(XNode.DeepEquals(myInputXml, body));
                                                              })
                 .Returns(encryptResponse);

                var encryptedXml = encryptor.Encrypt(myInputXml);

                Assert.Equal(typeof(KmsXmlDecryptor), encryptedXml.DecryptorType);
                var encryptedBlob = (string)encryptedXml.EncryptedElement.Element("value");
                Assert.Equal(myBase64EncryptedData, encryptedBlob);
            }
        }

        [Fact]
        public void EnsureContextsAreUnaltered()
        {
            Assert.Equal("AspNetCore.DataProtection.Aws.Kms.Xml", KmsConstants.DefaultEncryptionContextKey);
            Assert.Equal("b7b7f5af-d3c3-436d-8792-87dfd65e1cd4", KmsConstants.DefaultEncryptionContextValue);
            Assert.Equal("AspNetCore.DataProtection.Aws.Kms.Xml.ApplicationName", KmsConstants.ApplicationEncryptionContextKey);
        }
    }
}
