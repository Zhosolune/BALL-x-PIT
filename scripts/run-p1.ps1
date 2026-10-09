<#
.SYNOPSIS
使用工作区 SDK 构建并运行 P1 离线场景；-Demo 运行人工推荐示例。
#>
param([switch]$Demo)

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$sdkPath = Join-Path $workspacePath '.tools/dotnet/dotnet.exe'
$projectPath = Join-Path $workspacePath 'tests/DecisionCore.Checks/DecisionCore.Checks.csproj'
if (-not (Test-Path -LiteralPath $sdkPath)) {
    throw '工作区 SDK 未安装，请先按 docs/p1-core.md 的步骤准备 .NET SDK 8.0.425。'
}

# 保存进程配置，脚本结束后恢复；无需全局修改 PATH。
$previousLocation = Get-Location
$settings = @{
    DOTNET_CLI_HOME = (Join-Path $workspacePath '.dotnet-home')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    NUGET_PACKAGES = (Join-Path $workspacePath '.nuget')
}
$previousValues = @{}
try {
    foreach ($name in $settings.Keys) {
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    Set-Location -LiteralPath $workspacePath
    & $sdkPath build $projectPath -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'P1 构建失败。' }
    if ($Demo) {
        & $sdkPath run --project $projectPath -c Release --no-build -- --demo
    } else {
        & $sdkPath run --project $projectPath -c Release --no-build
    }
    if ($LASTEXITCODE -ne 0) { throw 'P1 场景执行失败。' }
} finally {
    Set-Location -LiteralPath $previousLocation.Path
    foreach ($name in $previousValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process')
    }
}
