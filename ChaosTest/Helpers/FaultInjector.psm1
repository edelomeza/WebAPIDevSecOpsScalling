#Requires -Version 5.1
Set-StrictMode -Version 2.0

function Get-DockerAvailable {
    return $null -ne (Get-Command docker -ErrorAction SilentlyContinue)
}

function Test-LinuxHost {
    if ($PSVersionTable.PSEdition -eq 'Core') { return [bool]$IsLinux }
    return $false
}

function Resolve-ChaosContainer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name
    )

    $id = $null
    $id = (& docker compose ps -aq $Name 2>$null | Where-Object { $_.Trim() } | Select-Object -First 1)
    if ($id) { return $id.Trim() }

    $id = (& docker ps -aq --filter "name=$Name" 2>$null | Where-Object { $_.Trim() } | Select-Object -First 1)
    if ($id) { return $id.Trim() }

    $id = (& docker ps -aq --filter "label=com.docker.compose.service=$Name" 2>$null | Where-Object { $_.Trim() } | Select-Object -First 1)
    if ($id) { return $id.Trim() }

    return $null
}

function Invoke-ChaosFault {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('kill', 'start', 'pause', 'unpause', 'latency', 'latency-off')]
        [string]$Action,
        [Parameter(Mandatory = $true)][string]$Target,
        [int]$LatencyMs = 2000
    )

    $result = [pscustomobject]@{
        Fault = $Action
        Target = $Target
        Success = $false
        Skipped = $false
        Message = ''
        ExitCode = 1
    }

    if (-not (Get-DockerAvailable)) {
        $result.Message = 'Docker no disponible (docker no encontrado en PATH)'
        return $result
    }

    $id = Resolve-ChaosContainer -Name $Target
    if (-not $id) {
        $result.Message = "Contenedor '$Target' no encontrado (compose service, name o label)"
        return $result
    }

    switch ($Action) {
        'kill' {
            $null = & docker kill $id 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "docker kill $id"
        }
        'start' {
            $null = & docker start $id 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "docker start $id"
        }
        'pause' {
            $null = & docker pause $id 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "docker pause $id"
        }
        'unpause' {
            $null = & docker unpause $id 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "docker unpause $id"
        }
        'latency' {
            if (-not (Test-LinuxHost)) {
                $result.Skipped = $true
                $result.ExitCode = 0
                $result.Message = "Inyección de latencia requiere host Linux (tc netem). En Windows usar 'pause' como proxy de degradación"
                return $result
            }
            $null = & docker exec $id tc qdisc add dev eth0 root netem delay "$LatencyMs"ms 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "tc netem delay ${LatencyMs}ms en $id"
        }
        'latency-off' {
            if (-not (Test-LinuxHost)) {
                $result.Skipped = $true
                $result.ExitCode = 0
                $result.Message = 'Sin inyección previa en host no Linux'
                return $result
            }
            $null = & docker exec $id tc qdisc del dev eth0 root netem 2>$null
            $result.ExitCode = $LASTEXITCODE
            $result.Success = ($LASTEXITCODE -eq 0)
            $result.Message = "tc netem eliminado en $id"
        }
    }
    return $result
}

function Invoke-ChaosVerify {
    [CmdletBinding()]
    param(
        [string]$Method = 'GET',
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$ExpectedStatus = 200,
        [int]$TimeoutSec = 10
    )
    $status = 0
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method $Method -TimeoutSec $TimeoutSec -UseBasicParsing
        $status = [int]$resp.StatusCode
    }
    catch {
        $respProp = $_.Exception.PSObject.Properties['Response']
        $respObj = $null
        if ($null -ne $respProp) { $respObj = $respProp.Value }
        if ($null -ne $respObj) {
            $statusProp = $respObj.PSObject.Properties['StatusCode']
            if ($null -ne $statusProp) { $status = [int]$statusProp.Value }
        }
    }
    return [pscustomobject]@{
        Url = $Url
        Status = $status
        Expected = $ExpectedStatus
        Passed = ($status -eq $ExpectedStatus)
    }
}

Export-ModuleMember -Function Get-DockerAvailable, Test-LinuxHost, Resolve-ChaosContainer, Invoke-ChaosFault, Invoke-ChaosVerify
