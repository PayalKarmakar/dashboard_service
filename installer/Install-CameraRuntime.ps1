# Installs Python (if missing) and CameraService venv + pip packages.
# Called by SRP-Setup.exe after files are copied.
param(
    [Parameter(Mandatory = $true)]
    [string]$CameraServiceDir,

    [string]$PythonInstaller = ""
)

$ErrorActionPreference = "Stop"
$logPath = Join-Path $CameraServiceDir "camera-runtime-install.log"

function Write-Log([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Refresh-Path {
    $machine = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machine;$user"
}

function Find-Python {
    Refresh-Path

    $candidates = @(
        "${env:ProgramFiles}\Python312\python.exe",
        "${env:ProgramFiles}\Python311\python.exe",
        "${env:LocalAppData}\Programs\Python\Python312\python.exe",
        "${env:LocalAppData}\Programs\Python\Python311\python.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    foreach ($cmd in @("py", "python")) {
        try {
            $resolved = Get-Command $cmd -ErrorAction Stop
            if ($cmd -eq "py") {
                $out = & $resolved.Source -3.12 -c "import sys; print(sys.executable)" 2>$null
                if (-not $out) {
                    $out = & $resolved.Source -3 -c "import sys; print(sys.executable)" 2>$null
                }
                if ($out -and (Test-Path $out.Trim())) {
                    return $out.Trim()
                }
            }
            else {
                $out = & $resolved.Source -c "import sys; print(sys.executable)" 2>$null
                if ($out -and (Test-Path $out.Trim()) -and ($out -notmatch "WindowsApps")) {
                    return $out.Trim()
                }
            }
        }
        catch { }
    }

    return $null
}

function Install-Python([string]$InstallerPath) {
    if (-not (Test-Path $InstallerPath)) {
        throw "Python installer not found: $InstallerPath"
    }

    Write-Log "Installing Python silently from $InstallerPath ..."
    $args = @(
        "/quiet",
        "InstallAllUsers=1",
        "PrependPath=1",
        "Include_test=0",
        "Include_launcher=1",
        "Include_pip=1"
    )

    $p = Start-Process -FilePath $InstallerPath -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        throw "Python installer failed with exit code $($p.ExitCode)"
    }

    Refresh-Path
    Start-Sleep -Seconds 2
}

try {
    if (-not (Test-Path $CameraServiceDir)) {
        throw "CameraService folder not found: $CameraServiceDir"
    }

    Write-Log "=== Camera runtime setup start ==="
    Write-Log "CameraServiceDir=$CameraServiceDir"

    $python = Find-Python
    if (-not $python) {
        if ([string]::IsNullOrWhiteSpace($PythonInstaller)) {
            $PythonInstaller = Join-Path (Split-Path $CameraServiceDir -Parent) "Prereqs\python-installer.exe"
        }
        Install-Python -InstallerPath $PythonInstaller
        $python = Find-Python
        if (-not $python) {
            throw "Python was installed but could not be located on PATH."
        }
    }

    Write-Log "Using Python: $python"
    & $python -c "import sys; print(sys.version)"
    if ($LASTEXITCODE -ne 0) { throw "Python check failed" }

    $venvDir = Join-Path $CameraServiceDir ".venv"
    $venvPython = Join-Path $venvDir "Scripts\python.exe"
    $requirements = Join-Path $CameraServiceDir "requirements.txt"

    if (-not (Test-Path $requirements)) {
        throw "requirements.txt missing: $requirements"
    }

    if (-not (Test-Path $venvPython)) {
        Write-Log "Creating virtual environment..."
        if (Test-Path $venvDir) {
            Remove-Item $venvDir -Recurse -Force
        }
        & $python -m venv $venvDir
        if ($LASTEXITCODE -ne 0) { throw "Failed to create venv" }
    }

    if (-not (Test-Path $venvPython)) {
        throw "venv python missing after create: $venvPython"
    }

    Write-Log "Upgrading pip..."
    & $venvPython -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed" }

    Write-Log "Installing CameraService requirements (this may take several minutes)..."
    & $venvPython -m pip install -r $requirements
    if ($LASTEXITCODE -ne 0) { throw "pip install -r requirements.txt failed" }

    Write-Log "=== Camera runtime setup OK ==="
    exit 0
}
catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    exit 1
}
