#Requires -Version 5.1
<#
.SYNOPSIS
Ejecuta un experimento de chaos definido en JSON sobre contenedores Docker.
.DESCRIPTION
Carga un experimento (name, steps, recovery; load opcional para arrancar carga NBomber),
aplica cada fallo via Helpers\FaultInjector.psm1, verifica el estado esperado de la API y
escribe un reporte JSON en reports/ (raíz del repo).
Exit codes: 0 = PASS, 1 = al menos un paso/verificación falló, 2 = error de suite (JSON inválido,
experimento inexistente, faltan parámetros).
.EXAMPLE
powershell -File run-chaos.ps1 -ListFaults
powershell -File run-chaos.ps1 -Experiment .\Experiments\redis-kill.json
powershell -File run-chaos.ps1 -Experiment .\Experiments\redis-kill.json -DryRun
#>
[CmdletBinding()]
param(
    [string]$Experiment,
    [switch]$ListFaults,
    [switch]$DryRun,
    [string]$ReportDir
)

Set-StrictMode -Version 2.0

if (-not $ReportDir) {
    $ReportDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'reports'
}
$repoRoot = Split-Path -Parent $PSScriptRoot

Import-Module (Join-Path $PSScriptRoot 'Helpers\FaultInjector.psm1') -Force

function Get-JsonProp {
    param($Obj, [string]$Name)
    $prop = $Obj.PSObject.Properties[$Name]
    if ($null -ne $prop) { return $prop.Value }
    return $null
}

function Stop-LoadTree {
    param($Process)
    if (-not $Process) { return }
    $Process.Refresh()
    if ($Process.HasExited) { return }
    $procId = $Process.Id
    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        & taskkill /PID $procId /T /F 2>$null | Out-Null
    }
    else {
        & pkill -TERM -P $procId 2>$null
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    }
}

function Show-FaultCatalog {
    $catalog = @(
        'kill          - Mata el contenedor (SIGKILL) para simular caída de infraestructura',
        'start         - Arranca el contenedor (recuperación tras kill)',
        'pause         - Congela el contenedor (proxy de degradación en cualquier host)',
        'unpause       - Reanuda el contenedor',
        'latency       - Inyecta latencia de red vía tc netem (solo host Linux)',
        'latency-off   - Elimina la latencia inyectada'
    )
    Write-Host 'Fallos soportados:' -ForegroundColor Cyan
    $catalog | ForEach-Object { Write-Host "  $_" }
}

if ($ListFaults) {
    Show-FaultCatalog
    exit 0
}

if (-not $Experiment) {
    Write-Host 'Uso: run-chaos.ps1 -Experiment <experiment.json> [-DryRun]' -ForegroundColor Yellow
    exit 2
}

if (-not (Test-Path -LiteralPath $Experiment)) {
    Write-Host "ERROR: experimento no encontrado: $Experiment" -ForegroundColor Red
    exit 2
}

$exp = $null
try {
    $exp = Get-Content -LiteralPath $Experiment -Raw | ConvertFrom-Json
}
catch {
    Write-Host "ERROR: JSON inválido: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

$expName = Get-JsonProp $exp 'name'
$steps = Get-JsonProp $exp 'steps'
if (-not $expName -or -not $steps) {
    Write-Host 'ERROR: el experimento debe tener "name" y "steps"' -ForegroundColor Red
    exit 2
}

Write-Host "==> Experiment: $expName" -ForegroundColor Cyan
$expDescription = Get-JsonProp $exp 'description'
if ($expDescription) { Write-Host "    $expDescription" }

$results = @()
$failed = $false
$loadProcess = $null
$savedEnv = @{}

try {
    $load = Get-JsonProp $exp 'load'
    if ($load -and -not $DryRun) {
        $loadCommand = Get-JsonProp $load 'command'
        $loadArgs = Get-JsonProp $load 'args'
        if (-not $loadArgs) { $loadArgs = @() }
        $warmup = Get-JsonProp $load 'warmupSeconds'
        if (-not $warmup) { $warmup = 10 }

        $loadEnv = Get-JsonProp $load 'env'
        if ($loadEnv) {
            foreach ($p in $loadEnv.PSObject.Properties) {
                $savedEnv[$p.Name] = [Environment]::GetEnvironmentVariable($p.Name)
                [Environment]::SetEnvironmentVariable($p.Name, [string]$p.Value)
            }
        }

        if (-not $loadCommand) {
            Write-Host '  [!] sección load sin "command"; se omite la carga' -ForegroundColor Yellow
        }
        else {
            try {
                if (-not (Test-Path -LiteralPath $ReportDir)) {
                    $null = New-Item -ItemType Directory -Path $ReportDir
                }
                $loadStamp = Get-Date -Format yyyyMMdd-HHmmss
                $loadLogOut = Join-Path $ReportDir "load-$expName-$loadStamp.out.log"
                $loadLogErr = Join-Path $ReportDir "load-$expName-$loadStamp.err.log"
                Write-Host "  ==> Carga: $loadCommand $($loadArgs -join ' ') (warmup ${warmup}s)" -ForegroundColor Cyan
                $loadProcess = Start-Process -FilePath $loadCommand -ArgumentList $loadArgs -WorkingDirectory $repoRoot -RedirectStandardOutput $loadLogOut -RedirectStandardError $loadLogErr -PassThru -NoNewWindow
                Start-Sleep -Seconds $warmup
                $loadProcess.Refresh()
                if ($loadProcess.HasExited) {
                    Write-Host "      WARN: la carga terminó prematuramente (exit $($loadProcess.ExitCode)); logs: $loadLogOut / $loadLogErr" -ForegroundColor DarkYellow
                }
                else {
                    Write-Host "      Carga activa (PID $($loadProcess.Id))" -ForegroundColor DarkGray
                }
            }
            catch {
                Write-Host "      WARN: no se pudo iniciar la carga: $($_.Exception.Message)" -ForegroundColor DarkYellow
            }
        }
    }

    foreach ($step in $steps) {
        $action = Get-JsonProp $step 'action'
        $target = Get-JsonProp $step 'target'
        if (-not $target) { $target = Get-JsonProp $exp 'target' }
        $stepName = Get-JsonProp $step 'name'
        if (-not $stepName) { $stepName = "$action $target" }

        if (-not $action -or -not $target) {
            Write-Host "  [!] ${stepName}: falta action/target" -ForegroundColor Yellow
            $failed = $true
            continue
        }

        Write-Host "  ==> $stepName" -ForegroundColor Green

        if ($DryRun) {
            Write-Host "      (dry-run) $action $target" -ForegroundColor DarkGray
            continue
        }

        $fault = Invoke-ChaosFault -Action $action -Target $target -LatencyMs (Get-JsonProp $step 'latencyMs')
        $stepResult = [pscustomobject]@{
            Step = $stepName
            Fault = $fault.Fault
            Target = $fault.Target
            Skipped = $fault.Skipped
            Success = $fault.Success
            Message = $fault.Message
            Verify = $null
        }

        if ($fault.Skipped) {
            Write-Host "      (skip) $($fault.Message)" -ForegroundColor DarkYellow
        }
        elseif (-not $fault.Success) {
            Write-Host "      FAIL: $($fault.Message)" -ForegroundColor Red
            $failed = $true
        }
        else {
            Write-Host "      OK: $($fault.Message)" -ForegroundColor DarkGray
        }

        $verify = Get-JsonProp $step 'verify'
        if ($verify -and ($fault.Success -or $fault.Skipped)) {
            $vUrl = Get-JsonProp $verify 'url'
            $vMethod = Get-JsonProp $verify 'method'
            if (-not $vMethod) { $vMethod = 'GET' }
            $vStatus = Get-JsonProp $verify 'expectedStatus'
            if (-not $vStatus) { $vStatus = 200 }
            $check = $null
            for ($attempt = 1; $attempt -le 5; $attempt++) {
                $check = Invoke-ChaosVerify -Url $vUrl -Method $vMethod -ExpectedStatus $vStatus
                if ($check.Passed -or $attempt -eq 5) { break }
                Start-Sleep -Seconds 5
            }
            $stepResult.Verify = $check
            if ($check.Passed) {
                Write-Host "      verify OK: $vMethod $vUrl -> $($check.Status)" -ForegroundColor DarkGray
            }
            else {
                Write-Host "      verify FAIL: $vMethod $vUrl -> $($check.Status) (esperado $vStatus)" -ForegroundColor Red
                $failed = $true
            }
        }
        $results += $stepResult
    }
}
finally {
    if ($loadProcess -and -not $DryRun) {
        $loadProcess.Refresh()
        if (-not $loadProcess.HasExited) {
            Stop-LoadTree -Process $loadProcess
            Write-Host '  ==> Carga detenida' -ForegroundColor Cyan
        }
    }
    foreach ($k in $savedEnv.Keys) {
        [Environment]::SetEnvironmentVariable($k, $savedEnv[$k])
    }
    $recovery = Get-JsonProp $exp 'recovery'
    if ($recovery -and -not $DryRun) {
        Write-Host '  ==> Recuperación (finally)' -ForegroundColor Cyan
        foreach ($r in $recovery) {
            $action = Get-JsonProp $r 'action'
            $target = Get-JsonProp $r 'target'
            if (-not $target) { $target = Get-JsonProp $exp 'target' }
            $fault = Invoke-ChaosFault -Action $action -Target $target -LatencyMs (Get-JsonProp $r 'latencyMs')
            if ($fault.Success -or $fault.Skipped) {
                Write-Host "      OK: $($fault.Message)" -ForegroundColor DarkGray
            }
            else {
                Write-Host "      WARN: $($fault.Message)" -ForegroundColor DarkYellow
            }
        }
    }
}

$finalCode = 0
if ($failed) {
    $finalCode = 1
    Write-Host 'RESULTADO: FAIL' -ForegroundColor Red
}
else {
    Write-Host 'RESULTADO: PASS' -ForegroundColor Green
}

if (-not $DryRun) {
    $report = [pscustomobject]@{
        Experiment = $expName
        Timestamp = (Get-Date).ToString('o')
        ExitCode = $finalCode
        Results = $results
    }
    try {
        if (-not (Test-Path -LiteralPath $ReportDir)) {
            $null = New-Item -ItemType Directory -Path $ReportDir
        }
        $reportFile = Join-Path $ReportDir "chaos-$expName-$(Get-Date -Format yyyyMMdd-HHmmss).json"
        $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportFile -Encoding UTF8
        Write-Host "Reporte: $reportFile" -ForegroundColor Cyan
    }
    catch {
        Write-Host "WARN: no se pudo escribir el reporte: $($_.Exception.Message)" -ForegroundColor DarkYellow
    }
}

exit $finalCode
