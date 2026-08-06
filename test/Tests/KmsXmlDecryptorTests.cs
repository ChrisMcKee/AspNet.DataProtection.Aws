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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FakeItEasy;
using Xunit;

namespace AspNetCore.DataProtection.Aws.Tests
{
    public class KmsXmlDecryptorTests
    {
        private readonly KmsXmlDecryptor decryptor;
        private readonly IAmazonKeyManagementService kmsClient;
        private readonly IOptions<KmsXmlEncryptorConfig> encryptConfig;
        private readonly IOptions<DataProtectionOptions> dpOptions;
        private const string KeyId = "keyId";
        private const string ElementName = "name";
        private readonly Dictionary<string, string> encryptionContext = new Dictionary<string, string>();
        private readonly List<string> grantTokens = new List<string>();

        public KmsXmlDecryptorTests()
        {
            kmsClient = A.Fake<IAmazonKeyManagementService>(o => o.Strict());
            encryptConfig = A.Fake<IOptions<KmsXmlEncryptorConfig>>(o => o.Strict());
            dpOptions = A.Fake<IOptions<DataProtectionOptions>>(o => o.Strict());
            var serviceProvider = A.Fake<IServiceProvider>(o => o.Strict());

            A.CallTo(() => serviceProvider.GetService(typeof(IOptions<KmsXmlEncryptorConfig>)))
             .Returns(encryptConfig);
            A.CallTo(() => serviceProvider.GetService(typeof(IOptions<DataProtectionOptions>)))
             .Returns(dpOptions);
            A.CallTo(() => serviceProvider.GetService(typeof(IAmazonKeyManagementService)))
             .Returns(kmsClient);
            A.CallTo(() => serviceProvider.GetService(typeof(ILoggerFactory)))
             .Returns(null as ILoggerFactory);

            decryptor = new KmsXmlDecryptor(serviceProvider);
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
            var myEncryptedString = "encrypted";
            var myBase64EncryptedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(myEncryptedString));
            var myEncryptedXml = new XElement(ElementName, new XElement("value", myBase64EncryptedData));
            var myOutputXml = new XElement(ElementName, "output");

            using(var decryptedResponseStream = new MemoryStream())
            {
                myOutputXml.Save(decryptedResponseStream);
                decryptedResponseStream.Seek(0, SeekOrigin.Begin);

                var decryptResponse = new DecryptResponse { KeyId = KeyId, Plaintext = decryptedResponseStream };

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

                A.CallTo(() => kmsClient.DecryptAsync(A<DecryptRequest>._, CancellationToken.None))
                 .Invokes((DecryptRequest dr, CancellationToken ct) =>
                                                              {
                                                                  if(appId != null && useAppId)
                                                                  {
                                                                      Assert.Contains(KmsConstants.ApplicationEncryptionContextKey, dr.EncryptionContext.Keys);
                                                                      Assert.Equal(expectedAppId, dr.EncryptionContext[KmsConstants.ApplicationEncryptionContextKey]);
                                                                  }
                                                                  else
                                                                  {
                                                                      Assert.Same(encryptionContext, dr.EncryptionContext);
                                                                  }

                                                                  Assert.Same(grantTokens, dr.GrantTokens);

                                                                  Assert.Equal(myEncryptedString, Encoding.UTF8.GetString(dr.CiphertextBlob.ToArray()));
                                                              })
                 .Returns(decryptResponse);

                var plaintextXml = decryptor.Decrypt(myEncryptedXml);
                Assert.True(XNode.DeepEquals(myOutputXml, plaintextXml));
            }
        }
    }
}
