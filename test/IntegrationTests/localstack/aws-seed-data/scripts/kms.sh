#!/bin/bash

set -e  # Exit on any error

echo "Waiting for KMS service to be ready..."
sleep 10

echo "Checking if KMS service is available..."
for i in {1..30}; do
    if awslocal kms list-keys >/dev/null 2>&1; then
        echo "KMS service is ready"
        break
    fi
    echo "Waiting for KMS service... attempt $i/30"
    sleep 2
done

echo "Creating KMS key for integration tests..."

# Create the KMS key and capture the output
create_key_output=$(awslocal kms create-key --key-usage ENCRYPT_DECRYPT --key-spec RSA_2048)

echo "KMS key creation output: $create_key_output"

# Extract the KeyId from the output using a more robust approach
# Try multiple extraction methods to handle different JSON formatting
key_id=$(echo "$create_key_output" | grep -o '"KeyId"[^"]*"[^"]*"' | sed 's/.*"KeyId"[^"]*"\([^"]*\)".*/\1/')

# If that fails, try a simpler approach
if [ -z "$key_id" ]; then
    key_id=$(echo "$create_key_output" | grep -o 'KeyId[^,]*' | sed 's/KeyId[^"]*"\([^"]*\)".*/\1/')
fi

# If that still fails, try extracting UUID pattern directly
if [ -z "$key_id" ]; then
    key_id=$(echo "$create_key_output" | grep -o '[a-f0-9]\{8\}-[a-f0-9]\{4\}-[a-f0-9]\{4\}-[a-f0-9]\{4\}-[a-f0-9]\{12\}')
fi

echo "Extracted KeyId: '$key_id'"

if [ "$key_id" = "null" ] || [ -z "$key_id" ]; then
    echo "ERROR: Failed to extract KeyId from KMS key creation response"
    echo "Raw output was: $create_key_output"
    exit 1
fi

# Create the alias using the extracted KeyId
echo "Creating alias for KeyId: $key_id"
awslocal kms create-alias --alias-name alias/KmsIntegrationTesting --target-key-id $key_id

echo "Verifying KMS key and alias..."
awslocal kms describe-key --key-id alias/KmsIntegrationTesting

echo "KMS key and alias created successfully"
