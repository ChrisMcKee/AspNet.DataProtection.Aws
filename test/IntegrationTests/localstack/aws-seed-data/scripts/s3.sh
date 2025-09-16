#!/bin/sh

echo "Creating S3 bucket for integration tests..."
awslocal s3 mb s3://dataprotection-s3-integration-tests
echo "S3 bucket created successfully"