# Short entry point for the native client bridge. Passwords are prompted privately by Python.
param([string]$Command = 'game:status', [string]$Text, [switch]$Stdio, [switch]$PasswordStdin, [string]$Session)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$python = Join-Path $repo '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { $python = (Get-Command python -ErrorAction Stop).Source }
$arguments = @((Join-Path $PSScriptRoot 'client_control.py'), $Command)
if ($PSBoundParameters.ContainsKey('Text')) { $arguments += $Text }
if ($Stdio) { $arguments += '--stdio' }
if ($PasswordStdin) { $arguments += '--password-stdin' }
if ($Session) { $arguments += '--session', $Session }
Push-Location $repo
try { & $python @arguments; exit $LASTEXITCODE } finally { Pop-Location }
