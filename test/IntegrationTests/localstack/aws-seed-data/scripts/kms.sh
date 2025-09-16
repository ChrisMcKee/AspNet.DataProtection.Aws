#!/bin/bash

apt install jq -yq

# Create the KMS key and capture the output
create_key_output=$(awslocal kms create-key --key-usage ENCRYPT_DECRYPT --key-spec RSA_2048)

# Extract the KeyId from the output
key_id=$(echo $create_key_output | jq -r '.KeyMetadata.KeyId')

# Create the alias using the extracted KeyId
awslocal kms create-alias --alias-name alias/KmsIntegrationTesting --target-key-id $key_id
