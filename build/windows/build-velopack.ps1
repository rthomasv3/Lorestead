# Build script for the Lorestead Windows installer via Velopack (vpk pack).
#
# Usage: ./build-velopack.ps1 -Version <semver> [-SignFile <signing-metadata.json>]
#
# Build process:
# 1. Publish the client AOT for win-x64 (the csproj builds the frontend in Release)
# 2. Publish the MCP server AOT and stage it beside the client
# 3. Package with Velopack (vpk pack); -SignFile enables Azure Trusted Signing

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$SignFile
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$ProjectRoot = (Get-Item (Join-Path $ScriptDir '..\..')).FullName
$ClientCsproj = Join-Path $ProjectRoot 'src\Lorestead.Client\Lorestead.Client.csproj'
$McpCsproj = Join-Path $ProjectRoot 'src\Lorestead.Mcp\Lorestead.Mcp.csproj'
$PublishDir = Join-Path $ScriptDir 'tmp\publish'
$McpPublishDir = Join-Path $ScriptDir 'tmp\publish-mcp'
$OutputDir = Join-Path $ScriptDir 'output'
$IconPath = Join-Path $ProjectRoot 'icon\icon.ico'

Write-Host '=== Building Lorestead Windows installer ==='
Write-Host "Project root: $ProjectRoot"
Write-Host "Publish dir:  $PublishDir"
Write-Host "Output dir:   $OutputDir"
Write-Host "Version:      $Version"

# ============================================================================
# Step 1: Publish client AOT
# ============================================================================
Write-Host "`n[1/3] Publishing client AOT build..."
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# MinVerVersionOverride keeps the binary's stamped version identical to the pack
# version even when the working tree isn't at the release tag.
dotnet publish $ClientCsproj `
    -c Release `
    -r win-x64 `
    -p:MinVerVersionOverride=$Version `
    -p:NativeDebugSymbols=false `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw 'client dotnet publish failed' }

# ============================================================================
# Step 2: Publish MCP server AOT and stage it beside the client
# ============================================================================
Write-Host "`n[2/3] Publishing MCP server AOT build..."
if (Test-Path $McpPublishDir) { Remove-Item -Recurse -Force $McpPublishDir }
New-Item -ItemType Directory -Path $McpPublishDir -Force | Out-Null

dotnet publish $McpCsproj `
    -c Release `
    -r win-x64 `
    -p:MinVerVersionOverride=$Version `
    -p:NativeDebugSymbols=false `
    -o $McpPublishDir
if ($LASTEXITCODE -ne 0) { throw 'MCP dotnet publish failed' }

# The MCP exe ships inside the client install (decisions.md) so agents spawn it
# from the stable %LocalAppData%\Lorestead\current\ path and it can never
# version-skew against the app.
Get-ChildItem $McpPublishDir -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $PublishDir -Force

# ============================================================================
# Step 3: Package with Velopack
# ============================================================================
Write-Host "`n[3/3] Packaging with Velopack..."
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# vpk targets an older runtime than the project's .NET 10; roll-forward bridges that.
$env:DOTNET_ROLL_FORWARD = 'Major'

# Pack id is plain 'Lorestead' (not reverse-DNS) so the install lands in
# %LocalAppData%\Lorestead\current\ - the path agents-and-mcp.md and the seed
# content document for the MCP exe.
$packArgs = @(
    '--packId', 'Lorestead',
    '--packVersion', $Version,
    '--packDir', $PublishDir,
    '--mainExe', 'Lorestead.exe',
    '--packTitle', 'Lorestead',
    '--icon', $IconPath,
    '--channel', 'win-x64',
    '--outputDir', $OutputDir
)

if ($SignFile) {
    $packArgs += @('--azureTrustedSignFile', (Resolve-Path $SignFile).Path)
}

vpk pack @packArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Write-Host "`n=== Build Complete ==="
Get-ChildItem $OutputDir | Format-Table Name, Length
Write-Host "`nOutput directory: $OutputDir"
