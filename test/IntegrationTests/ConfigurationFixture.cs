// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AspNetCore.DataProtection.Aws.IntegrationTests
{
    public class ConfigurationFixture
    {
        public ConfigurationFixture()
        {
#if !NETCOREAPP
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, cert, chain, sslPolicyErrors) => { return true; };
#endif

            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                                                    .AddJsonFile("config.json");

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }
    }
}
