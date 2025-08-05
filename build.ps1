#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for SE Phyzix plugin
.DESCRIPTION
    Builds the SE Phyzix plugin with automatic deployment by default.
    Designed to be AI-friendly with sensible defaults.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Release.
.PARAMETER Verbosity
    MSBuild verbosity level. Default is minimal.
.PARAMETER Clean
    Clean build artifacts before building
.PARAMETER NoDeploy
    Skip automatic deployment (deployment is ON by default)
.PARAMETER UseDotnet
    Force use of dotnet CLI instead of MSBuild
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Clean
    .\build.ps1 -NoDeploy
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",
    
    [switch]$Clean,
    
    [switch]$NoDeploy,
    
    [switch]$UseDotnet
)

# Colors for output
$SuccessColor = "Green"
$ErrorColor = "Red"
$WarningColor = "Yellow"
$InfoColor = "Cyan"

Write-Host "===========================================" -ForegroundColor $InfoColor
Write-Host "  SE Phyzix Build Script" -ForegroundColor $InfoColor
Write-Host "===========================================" -ForegroundColor $InfoColor
Write-Host ""

# Find build tool
$buildTool = $null
$buildCommand = $null

if (-not $UseDotnet) {
    # Try to find MSBuild first
    $msbuildPaths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($path in $msbuildPaths) {
        if (Test-Path $path) {
            $buildTool = "MSBuild"
            $buildCommand = $path
            break
        }
    }
}

# Fallback to dotnet if MSBuild not found or UseDotnet specified
if (-not $buildCommand) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $buildTool = "dotnet"
        $buildCommand = "dotnet"
    } else {
        Write-Host "ERROR: Neither MSBuild nor dotnet CLI found." -ForegroundColor $ErrorColor
        Write-Host "Please install Visual Studio or .NET SDK." -ForegroundColor $ErrorColor
        exit 1
    }
}

Write-Host "Using build tool: $buildTool" -ForegroundColor $SuccessColor
if ($buildTool -eq "MSBuild") {
    Write-Host "Path: $buildCommand" -ForegroundColor $InfoColor
}
Write-Host ""

# Check if Directory.Build.props exists and has valid Bin64 path
if (Test-Path "Directory.Build.props") {
    $propsContent = Get-Content "Directory.Build.props" -Raw
    if ($propsContent -match '<Bin64>(.+)</Bin64>') {
        $bin64Path = $matches[1]
        if (Test-Path $bin64Path) {
            Write-Host "Using Bin64 path from Directory.Build.props: $bin64Path" -ForegroundColor $SuccessColor
        } else {
            Write-Host "WARNING: Bin64 path in Directory.Build.props not found: $bin64Path" -ForegroundColor $WarningColor
            Write-Host "Please update Directory.Build.props with correct Space Engineers path" -ForegroundColor $WarningColor
            exit 1
        }
    }
} else {
    Write-Host "ERROR: Directory.Build.props not found" -ForegroundColor $ErrorColor
    exit 1
}

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning solution..." -ForegroundColor $InfoColor
    if (Test-Path ".\Clean.bat") {
        & .\Clean.bat
    } else {
        Write-Host "Clean.bat not found, skipping clean" -ForegroundColor $WarningColor
    }
    Write-Host ""
}

# Build the solution
Write-Host "Building SE Phyzix..." -ForegroundColor $InfoColor
Write-Host "Configuration: $Configuration" -ForegroundColor $InfoColor
Write-Host "Verbosity: $Verbosity" -ForegroundColor $InfoColor
Write-Host ""

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

if ($buildTool -eq "MSBuild") {
    $buildArgs = @(
        "Phyzix.sln",
        "/p:Configuration=$Configuration",
        "/v:$Verbosity",
        "/m"  # Enable parallel build
    )
    
    if ($Clean) {
        $buildArgs += "/t:Clean,Build"
    }
    
    if ($NoDeploy) {
        $buildArgs += "/p:PostBuildEvent="
    }
    
    & $buildCommand $buildArgs
} else {
    # Using dotnet CLI
    $buildArgs = @("build", "Phyzix.sln")
    
    if ($Clean) {
        Write-Host "Cleaning with dotnet..." -ForegroundColor $InfoColor
        & $buildCommand clean Phyzix.sln -c $Configuration
    }
    
    $buildArgs += "-c", $Configuration
    $buildArgs += "--verbosity", $Verbosity
    
    if ($NoDeploy) {
        $buildArgs += "/p:PostBuildEvent="
    }
    
    & $buildCommand $buildArgs
}

$buildExitCode = $LASTEXITCODE
$stopwatch.Stop()

Write-Host ""
Write-Host "Build completed in $($stopwatch.Elapsed.TotalSeconds.ToString('F2')) seconds" -ForegroundColor $InfoColor

if ($buildExitCode -eq 0) {
    Write-Host "Build SUCCEEDED" -ForegroundColor $SuccessColor
    
    # Show output info
    $outputPath = ".\ClientPlugin\bin\$Configuration\Phyzix.dll"
    if (Test-Path $outputPath) {
        $fileInfo = Get-Item $outputPath
        Write-Host ""
        Write-Host "Output file: $outputPath" -ForegroundColor $InfoColor
        Write-Host "File size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor $InfoColor
        Write-Host "Last modified: $($fileInfo.LastWriteTime)" -ForegroundColor $InfoColor
    }
    
    # Deployment is automatic unless -NoDeploy is specified
    if (-not $NoDeploy) {
        Write-Host ""
        Write-Host "Plugin has been deployed to Bin64\Plugins\Local\" -ForegroundColor $SuccessColor
        Write-Host "(Post-build event handles deployment automatically)" -ForegroundColor $InfoColor
    } else {
        Write-Host ""
        Write-Host "Deployment skipped (-NoDeploy flag set)" -ForegroundColor $WarningColor
        Write-Host "To deploy manually, run: .\ClientPlugin\deploy.bat" -ForegroundColor $InfoColor
    }
} else {
    Write-Host "Build FAILED with exit code $buildExitCode" -ForegroundColor $ErrorColor
    
    # Provide helpful error resolution tips
    Write-Host ""
    Write-Host "Common solutions:" -ForegroundColor $WarningColor
    Write-Host "  1. Ensure Space Engineers is installed" -ForegroundColor $InfoColor
    Write-Host "  2. Update Directory.Build.props with correct Bin64 path" -ForegroundColor $InfoColor
    Write-Host "  3. Try building with -Clean flag" -ForegroundColor $InfoColor
    Write-Host "  4. Check if .NET Framework 4.8.1 SDK is installed" -ForegroundColor $InfoColor
    
    exit $buildExitCode
}

Write-Host ""
Write-Host "===========================================" -ForegroundColor $InfoColor
Write-Host ""

# Quick command reference for AI/developers
Write-Host "Quick Commands:" -ForegroundColor $InfoColor
Write-Host "  .\build.ps1              # Build and deploy (default)" -ForegroundColor $InfoColor
Write-Host "  .\build.ps1 -Clean       # Clean build and deploy" -ForegroundColor $InfoColor
Write-Host "  .\build.ps1 -NoDeploy    # Build without deploying" -ForegroundColor $InfoColor
Write-Host "  .\build.ps1 -UseDotnet   # Use dotnet CLI instead of MSBuild" -ForegroundColor $InfoColor