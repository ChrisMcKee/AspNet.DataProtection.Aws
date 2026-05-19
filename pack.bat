echo off
SET VERSION=%1
IF [%VERSION%]==[] SET VERSION=2.4.0-local

dotnet build -c Release -p:%VERSION% AspNet.DataProtection.Aws.sln
dotnet pack src\AspNet.DataProtection.Aws.Kms\AspNet.DataProtection.Aws.Kms.csproj -c Release --include-symbols --nologo -o artifacts/ -p:PackageVersion=%VERSION%
dotnet pack src\AspNet.DataProtection.Aws.S3\AspNet.DataProtection.Aws.S3.csproj -c Release --include-symbols --nologo -o artifacts/ -p:PackageVersion=%VERSION%
