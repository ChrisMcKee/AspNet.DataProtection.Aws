#!/bin/sh

echo "Running initialization scripts..."

# Check if scripts directory exists
echo "Checking scripts directory..."
ls -la /scripts/ || echo "Scripts directory not found"

# Run S3 script
echo "Running S3 initialization..."
if [ -f "/scripts/s3.sh" ]; then
    #chmod +x /scripts/s3.sh
    /scripts/s3.sh
else
    echo "S3 script not found at /scripts/s3.sh"
fi

# Run KMS script
echo "Running KMS initialization..."
if [ -f "/scripts/kms.sh" ]; then
    #chmod +x /scripts/kms.sh
    /scripts/kms.sh
else
    echo "KMS script not found at /scripts/kms.sh"
fi

echo "Initialization complete"
exit 0