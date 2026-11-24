param([Parameter(Mandatory)][ValidateSet('A','B','C')][string]$Persona)
$ErrorActionPreference='Stop'
# Explicit user invocation only. Nothing is printed except a safe confirmation.
$files=@{
    A="$env:LOCALAPPDATA/UniNetGitHubAppStep6E2E/student-a.dpapi"
    B="$env:LOCALAPPDATA/UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi"
    C="$env:LOCALAPPDATA/UniNetGitHubFrontendSeed/student-c.dpapi"
}
$secret=(Get-Content -Raw -LiteralPath $files[$Persona]).Trim() | ConvertTo-SecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try { Set-Clipboard -Value ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
Write-Output "Student $Persona password copied locally. Paste only into the local E2E login, then clear the clipboard with Set-Clipboard -Value ''. Do not share or log it."
