# Shared helpers for the PowerShell entrypoints.

# $ErrorActionPreference governs PowerShell terminating errors only: it does not
# turn a non-zero exit code from a native executable such as python or dotnet
# into a failure. Without an explicit check after every native call these
# scripts report success even when the test suite is red.
function Assert-LastExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}
