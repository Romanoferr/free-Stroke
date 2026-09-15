# Publicacao reproduzivel do Epic Pencil para Windows x64.
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1 -SkipTests
#   powershell -ExecutionPolicy Bypass -File scripts\publish-win-x64.ps1 -NoZip
#
# O que faz:
#   1. le a versao de Directory.Build.props (<Version>, fonte unica)
#   2. falha se installer/inno/setup.iss divergir de versao
#   3. limpa dist/win-x64
#   4. restore + build Release + testes (a menos que -SkipTests)
#   5. publish self-contained win-x64 single-file, sem trimming
#   6. remove *.pdb do artefato e gera o .zip (a menos que -NoZip)
#   7. informa onde o executavel foi gerado
#
# Pre-requisitos: .NET SDK 9 (global.json), Windows x64. Sem VS, sem runtime
# pre-instalado na maquina de destino (self-contained).
#
# NOTA: arquivo intencionalmente so com ASCII (sem acentos). O PowerShell 5.1
# le .ps1 sem BOM como ANSI e caracteres UTF-8 quebram o parse do script.

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PropsFile = Join-Path $RepoRoot "Directory.Build.props"
$IssFile = Join-Path $RepoRoot "installer\inno\setup.iss"
$ShellProject = Join-Path $RepoRoot "src\EpicPencil.Shell\EpicPencil.Shell.csproj"
$DistRoot = Join-Path $RepoRoot "dist\win-x64"

function Get-PropsVersion {
    $xml = New-Object xml
    $xml.Load($PropsFile)
    $node = $xml.SelectSingleNode("//Version")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Directory.Build.props sem <Version>. Defina Major.Minor.Patch antes de publicar."
    }
    return $node.InnerText.Trim()
}

$Version = Get-PropsVersion
Write-Output "Epic Pencil - publish win-x64 - versao $Version"

# Guarda de sincronia: instalador e executavel precisam anunciar a mesma versao.
if (Test-Path -LiteralPath $IssFile) {
    $issText = Get-Content -LiteralPath $IssFile -Raw
    $pattern = '#define\s+AppVersion\s+"' + [regex]::Escape($Version) + '"'
    if ($issText -notmatch $pattern) {
        throw "Versao divergente: Directory.Build.props ($Version) diferente de #define AppVersion em installer/inno/setup.iss. Sincronize antes de publicar."
    }
    Write-Output "Versao sincronizada com installer/inno/setup.iss."
} else {
    Write-Warning "installer/inno/setup.iss nao encontrado - pulando checagem de sincronia."
}

# 1. Limpar artefatos anteriores
if (Test-Path -LiteralPath $DistRoot) {
    Write-Output "Limpando $DistRoot ..."
    Remove-Item -LiteralPath $DistRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $DistRoot | Out-Null

# 2. Restaurar dependencias
Write-Output "Restaurando ..."
& dotnet restore "$RepoRoot\EpicPencil.sln" --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet restore falhou." }

# 3. Compilar em Release
Write-Output "Compilando Release ..."
& dotnet build "$RepoRoot\EpicPencil.sln" -c Release --no-restore --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou." }

# 4. Executar testes (unitarios; self-test da Shell roda no passo 5b do checklist manual)
if (-not $SkipTests) {
    Write-Output "Testando ..."
    & dotnet test "$RepoRoot\EpicPencil.sln" -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet test falhou." }
} else {
    Write-Warning "Testes pulados (-SkipTests). Nao distribua sem rodar os testes ao menos uma vez nesta revisao."
}

# 5. Publicar o executavel (self-contained, win-x64, single-file, sem trimming)
$PublishDir = Join-Path $DistRoot "EpicPencil-$Version"
Write-Output "Publicando em $PublishDir ..."
& dotnet publish $ShellProject -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

# 5b. Enxugar simbolos do artefato (debug continua disponivel no build local em bin/)
Get-ChildItem -LiteralPath $PublishDir -Filter "*.pdb" -Recurse | Remove-Item -Force

$Exe = Join-Path $PublishDir "EpicPencil.Shell.exe"
if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Artefato inesperado: $Exe nao foi gerado."
}

# 6. Artefato final previsivel + .zip para compartilhamento
$sizeMB = ((Get-Item -LiteralPath $Exe).Length / 1MB)
Write-Output "Conteudo do artefato:"
Get-ChildItem -LiteralPath $PublishDir -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($PublishDir.Length + 1)
    Write-Output ("  " + $rel)
}

if (-not $NoZip) {
    $Zip = Join-Path $RepoRoot "dist\EpicPencil-win-x64-$Version.zip"
    # -Path (com wildcard) em vez de -LiteralPath: LiteralPath nao expande "*"
    # e o .zip silenciosamente nao era criado. -Force substitui zip anterior.
    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $Zip -Force
    Write-Output "ZIP: $Zip"
}

# 7. Resumo
$info = (Get-Item -LiteralPath $Exe).VersionInfo
$sizeText = "{0:N1} MB" -f $sizeMB
Write-Output ""
Write-Output "PUBLICADO COM EXITO"
Write-Output ("  Executavel : " + $Exe)
Write-Output ("  Tamanho    : " + $sizeText + " (self-contained, sem runtime a instalar)")
Write-Output ("  Versao     : " + $info.ProductVersion + " / File " + $info.FileVersion)
Write-Output ""
Write-Output "Validacao manual restante (maquina limpa sem .NET): copiar a pasta,"
Write-Output "rodar EpicPencil.Shell.exe e seguir o checklist em docs/DISTRIBUTION.md."
