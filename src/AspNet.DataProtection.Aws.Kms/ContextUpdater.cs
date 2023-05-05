﻿// Copyright(c) 2018 Jeff Hotchkiss, Modifications 2023 Chris McKee
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace AspNetCore.DataProtection.Aws.Kms
{
    /// <summary>
    /// Shared code for dealing with the complexity of getting the right encryption context values
    /// </summary>
    internal static class ContextUpdater
    {
        public static Dictionary<string, string> GetEncryptionContext(IKmsXmlEncryptorConfig config, DataProtectionOptions dpOptions)
        {
            var encryptionContext = config.EncryptionContext;

            // Set the application discriminator as part of the context of encryption, given the intent of the discriminator
            if(!config.DiscriminatorAsContext || string.IsNullOrEmpty(dpOptions.ApplicationDiscriminator))
            {
                return encryptionContext;
            }

            encryptionContext = encryptionContext.ToDictionary(x => x.Key, x => x.Value);

            var contextValue = dpOptions.ApplicationDiscriminator;

            // Some application discriminators (like the defaults) leak sensitive file paths, so hash them
            if(config.HashDiscriminatorContext)
            {
                using(var hasher = SHA256.Create())
                {
                    contextValue = Convert.ToBase64String(hasher.ComputeHash(Encoding.UTF8.GetBytes(contextValue)));
                }
            }

            encryptionContext[KmsConstants.ApplicationEncryptionContextKey] = contextValue;

            return encryptionContext;
        }
    }
}
