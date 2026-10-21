# Windows wrapper for scripts/visual-tests.sh (runs the browser suites in the Playwright Linux image that matches the
# pinned Microsoft.Playwright package).
#   pwsh scripts/visual-tests.ps1                    # compare against committed chromium-linux baselines
#   pwsh scripts/visual-tests.ps1 -Update            # write missing baselines, then review and commit them
#   pwsh scripts/visual-tests.ps1 -Update -Mode changed   # after a Playwright upgrade
#   pwsh scripts/visual-tests.ps1 -PrintImage        # print the image tag and exit
# The image tag comes from Directory.Packages.props; $env:BTP_PLAYWRIGHT_IMAGE overrides it.
param([switch] $Update, [ValidateSet('missing', 'changed', 'all')] [string] $Mode = 'missing', [switch] $PrintImage)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packages = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
if ($packages -notmatch 'Include="Microsoft\.Playwright"\s+Version="([^"]+)"') {
  throw 'Could not read the Microsoft.Playwright version from Directory.Packages.props'
}
$version = $Matches[1]
$image = if ($env:BTP_PLAYWRIGHT_IMAGE) { $env:BTP_PLAYWRIGHT_IMAGE } else { "mcr.microsoft.com/playwright/dotnet:v$version-noble" }
if ($PrintImage) { Write-Output $image; exit 0 }
$snapshots = if ($Update) { $Mode } else { 'none' }
Write-Output "Playwright $version, image $image, BTP_UPDATE_SNAPSHOTS=$snapshots"

$script = @'
set -e
if [ ! -x /opt/dotnet10/dotnet ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /opt/dotnet10
fi
export PATH=/opt/dotnet10:$PATH DOTNET_ROOT=/opt/dotnet10
dotnet test --project samples/Contoso.UI.Tests --artifacts-path /tmp/btp-artifacts -- --filter-class '*CrossHostingTests' --filter-class '*VisualTests' --filter-class '*InAppPlayTests'
'@

docker run --rm --init --ipc=host `
  -v "${root}:/repo" -w /repo `
  -v btp-nuget:/root/.nuget/packages `
  -v btp-dotnet:/opt/dotnet10 `
  -e "BTP_UPDATE_SNAPSHOTS=$snapshots" `
  -e BTP_PROJECT_DIR=/repo/samples/Contoso.UI.Tests `
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 `
  $image bash -lc $script
exit $LASTEXITCODE
