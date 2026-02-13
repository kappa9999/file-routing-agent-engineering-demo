param(
    [string]$ProjectId,
    [string]$SourcePath,
    [string]$DestinationPath,
    [string]$Action = "Copy",
    [string]$Category = "Unknown",
    [string]$LogRoot = "$env:ProgramData\FileRoutingAgent\ConnectorLogs",
    [string]$QueueRoot = "$env:ProgramData\FileRoutingAgent\ConnectorQueue",
    [switch]$DryRun,
    [switch]$SimulateFailure
)

$ErrorActionPreference = "Stop"

function Resolve-Value {
    param(
        [string]$Current,
        [string]$EnvironmentVariable
    )

    if (-not [string]::IsNullOrWhiteSpace($Current)) {
        return $Current
    }

    $fromEnv = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        return $fromEnv
    }

    return ""
}

function Write-ConnectorResult {
    param(
        [bool]$Success,
        [string]$Status,
        [string]$Message,
        [string]$TransactionId,
        [string]$SubmissionFile,
        [int]$ExitCode = 0
    )

    $payload = [ordered]@{
        connector = "projectwise_script_sample"
        status = $Status
        success = $Success
        externalTransactionId = $TransactionId
        message = $Message
        submissionFile = $SubmissionFile
        timestampUtc = [DateTime]::UtcNow.ToString("o")
    }

    Write-Output ($payload | ConvertTo-Json -Compress)
    exit $ExitCode
}

try {
    $ProjectId = Resolve-Value -Current $ProjectId -EnvironmentVariable "FRA_PROJECT_ID"
    $SourcePath = Resolve-Value -Current $SourcePath -EnvironmentVariable "FRA_SOURCE_PATH"
    $DestinationPath = Resolve-Value -Current $DestinationPath -EnvironmentVariable "FRA_DESTINATION_PATH"
    $Category = Resolve-Value -Current $Category -EnvironmentVariable "FRA_FILE_CATEGORY"
    $Action = Resolve-Value -Current $Action -EnvironmentVariable "FRA_ACTION"

    if ([string]::IsNullOrWhiteSpace($ProjectId)) {
        throw "ProjectId is required."
    }

    if ([string]::IsNullOrWhiteSpace($SourcePath)) {
        throw "SourcePath is required."
    }

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        throw "DestinationPath is required."
    }

    $sourceExists = Test-Path -LiteralPath $SourcePath
    $destinationExists = Test-Path -LiteralPath $DestinationPath
    $sourceInfo = if ($sourceExists) { Get-Item -LiteralPath $SourcePath } else { $null }
    $destinationInfo = if ($destinationExists) { Get-Item -LiteralPath $DestinationPath } else { $null }

    New-Item -Path $LogRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $QueueRoot -ItemType Directory -Force | Out-Null

    $transactionId = "pwstub-" + [Guid]::NewGuid().ToString("N")
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $submissionFile = Join-Path $QueueRoot ("publish_{0}_{1}.json" -f $timestamp, $transactionId)

    $submission = [ordered]@{
        transactionId = $transactionId
        createdUtc = [DateTime]::UtcNow.ToString("o")
        mode = if ($DryRun.IsPresent) { "dry_run" } else { "queue_only_sample" }
        projectId = $ProjectId
        action = $Action
        category = $Category
        sourcePath = $SourcePath
        sourceExists = $sourceExists
        sourceSizeBytes = if ($sourceInfo) { [int64]$sourceInfo.Length } else { 0 }
        destinationPath = $DestinationPath
        destinationExists = $destinationExists
        destinationSizeBytes = if ($destinationInfo) { [int64]$destinationInfo.Length } else { 0 }
        machine = $env:COMPUTERNAME
        user = $env:USERNAME
    }

    $submission | ConvertTo-Json -Depth 8 | Set-Content -Path $submissionFile -Encoding UTF8

    $logLine = [ordered]@{
        atUtc = [DateTime]::UtcNow.ToString("o")
        event = "connector_submission_created"
        transactionId = $transactionId
        projectId = $ProjectId
        sourcePath = $SourcePath
        destinationPath = $DestinationPath
        action = $Action
        category = $Category
        submissionFile = $submissionFile
        dryRun = $DryRun.IsPresent
    } | ConvertTo-Json -Compress

    Add-Content -Path (Join-Path $LogRoot "projectwise-connector.log") -Value $logLine

    if ($SimulateFailure.IsPresent) {
        Write-ConnectorResult `
            -Success:$false `
            -Status "simulated_failure" `
            -Message "Failure simulation flag was set." `
            -TransactionId $transactionId `
            -SubmissionFile $submissionFile `
            -ExitCode 2
    }

    if ($DryRun.IsPresent) {
        Write-ConnectorResult `
            -Success:$true `
            -Status "dry_run" `
            -Message "Dry-run mode: submission file created only." `
            -TransactionId $transactionId `
            -SubmissionFile $submissionFile `
            -ExitCode 0
    }

    Write-ConnectorResult `
        -Success:$true `
        -Status "queued_for_projectwise_review" `
        -Message "Sample connector queued publish metadata for ProjectWise processing." `
        -TransactionId $transactionId `
        -SubmissionFile $submissionFile `
        -ExitCode 0
}
catch {
    $transactionId = "pwstub-error-" + [Guid]::NewGuid().ToString("N")
    $message = $_.Exception.Message
    Write-ConnectorResult `
        -Success:$false `
        -Status "script_error" `
        -Message $message `
        -TransactionId $transactionId `
        -SubmissionFile "" `
        -ExitCode 1
}
