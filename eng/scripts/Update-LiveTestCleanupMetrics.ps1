#!/usr/bin/env pwsh

# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

<#
.SYNOPSIS
    Materializes live test resource group cleanup counts into Kusto.

.DESCRIPTION
    The live test cleanup pipeline ('automation - live-test-resource-cleanup') logs the number of
    resource groups it sees and deletes for each subscription on every run. pipeline-witness ingests
    those build logs into the BuildLogLine table in the Pipelines Kusto database.

    Querying BuildLogLine directly is far too slow to back a dashboard (a one day window takes a few
    seconds, but a two week window times out). This script therefore runs a small, bounded query over
    a recent window and appends the resulting rows into the LiveTestResourceGroupSnapshot table, which
    is tiny and fast to query.

    The append is idempotent: rows already present for a given BuildId/JobName are filtered out, so
    overlapping runs and retries will not create duplicates.

    Two log formats are supported:
      - 'CLEANUP_METRICS {json}' emitted by live-test-resource-cleanup.ps1, which carries the full
        set of counts plus subscription identity. Preferred when present.
      - The older 'Total Resource Groups:' / 'Total Resource Groups To Delete:' lines, used as a
        fallback so that historical runs are still captured.

.PARAMETER ClusterUri
    The Kusto cluster to write to.

.PARAMETER Database
    The Kusto database containing BuildLogLine and the target table.

.PARAMETER TableName
    The target table name.

.PARAMETER BuildDefinitionId
    The Azure DevOps definition id of the live test cleanup pipeline.

.PARAMETER LookbackDays
    How far back to scan BuildLogLine. Keep this small; the query cost grows steeply with the window.

.EXAMPLE
    ./Update-LiveTestCleanupMetrics.ps1 -WhatIf

    Shows the rows that would be appended without writing anything.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param (
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ClusterUri = 'https://azsdkengsys.westus2.kusto.windows.net',

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $Database = 'Pipelines',

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $TableName = 'LiveTestResourceGroupSnapshot',

    [Parameter()]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $BuildDefinitionId = 1357,

    [Parameter()]
    [ValidateRange(1, 7)]
    [int] $LookbackDays = 2
)

Set-StrictMode -Version 4
$ErrorActionPreference = 'Stop'

function Get-KustoAccessToken {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $Resource
    )

    $token = Get-AzAccessToken -ResourceUrl $Resource -AsSecureString
    return [System.Net.NetworkCredential]::new('', $token.Token).Password
}

function Invoke-KustoRequest {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string] $Endpoint,

        [Parameter(Mandatory = $true)]
        [string] $Command,

        [Parameter(Mandatory = $true)]
        [string] $AccessToken
    )

    $body = @{
        db         = $Database
        csl        = $Command
        properties = @{ Options = @{ servertimeout = '00:10:00' } }
    } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod -Method Post `
            -Uri "$ClusterUri/v1/rest/$Endpoint" `
            -Headers @{ Authorization = "Bearer $AccessToken"; Accept = 'application/json' } `
            -ContentType 'application/json' `
            -Body $body
    } catch {
        $detail = $_.ErrorDetails?.Message
        if (!$detail) { $detail = $_.Exception.Message }
        throw "Kusto $Endpoint request failed: $detail"
    }

    # Kusto reports partial failures in-band rather than via a non-success status code.
    $primary = $response.Tables[0]
    if ($primary.Columns.ColumnName -contains 'Exceptions') {
        throw "Kusto reported a partial query failure: $($primary.Rows | ConvertTo-Json -Depth 6)"
    }

    return $response
}

# Selects the rows to append. Written so that it can also be run on its own for ad hoc inspection.
# Note on types: integer literals are long in Kusto, so coalesce(toint(x), 0) yields a long. The
# explicit toint() wrappers keep both union branches on identical types; without them the union
# silently produces separate ToDelete/ToDelete_long columns.
$selectRows = @"
let lookback = ${LookbackDays}d;
let definitionId = $BuildDefinitionId;
let steps = BuildTimelineRecord
    | where LastChangedOn > ago(lookback + 2d)
    | where BuildDefinitionId == definitionId and Type == "Task"
    | project BuildId, LogId, JobName = replace_string(RecordName, " - Resource Cleanup", "");
let lines = BuildLogLine
    | where Timestamp > ago(lookback)
    | where BuildDefinitionId == definitionId
    | where Message has "CLEANUP_METRICS" or Message has "Total Resource Groups"
    | join kind=inner steps on BuildId, LogId;
let structured = lines
    | where Message startswith "CLEANUP_METRICS"
    | extend p = parse_json(extract(@"CLEANUP_METRICS\s+(\{.*\})", 1, Message))
    | where isnotnull(p)
    | project Timestamp = todatetime(p.timestamp), BuildId, JobName,
        SubscriptionId = tostring(p.subscriptionId), SubscriptionName = tostring(p.subscriptionName),
        Environment = tostring(p.environment), GroupFilter = tostring(p.groupFilter),
        TotalGroups = toint(p.totalGroups), ToDelete = toint(p.toDelete), ToClean = toint(p.toClean),
        ToDeleteSoon = toint(p.toDeleteSoon), ToDeleteLater = toint(p.toDeleteLater),
        Source = "structured";
let legacy = lines
    | where Message has "Total Resource Groups"
    | extend Value = toint(extract(@"Total Resource Groups(?: To Delete)?:\s*(\d+)", 1, Message))
    | summarize Timestamp = min(Timestamp),
                TotalGroupsRaw = maxif(Value, Message !has "To Delete"),
                ToDeleteRaw = maxif(Value, Message has "To Delete")
            by BuildId, JobName
    | where isnotnull(TotalGroupsRaw)
    | project Timestamp, BuildId, JobName, SubscriptionId = "", SubscriptionName = "",
              Environment = "", GroupFilter = "", TotalGroups = toint(TotalGroupsRaw),
              ToDelete = toint(coalesce(ToDeleteRaw, 0)), ToClean = int(null),
              ToDeleteSoon = int(null), ToDeleteLater = int(null), Source = "logscrape";
let candidates =
    union structured, (legacy | join kind=leftanti (structured | project BuildId, JobName) on BuildId, JobName);
// isfuzzy tolerates the target table not existing yet, so the very first run can bootstrap it.
let existing = union isfuzzy=true
    (datatable(BuildId:long, JobName:string)[]),
    ($TableName | where Timestamp > ago(lookback + 2d) | project BuildId, JobName);
candidates
| join kind=leftanti existing on BuildId, JobName
| project Timestamp, BuildId, JobName, SubscriptionId, SubscriptionName, Environment, GroupFilter,
          TotalGroups, ToDelete, ToClean, ToDeleteSoon, ToDeleteLater, Source
"@

$accessToken = Get-KustoAccessToken -Resource $ClusterUri

if ($WhatIfPreference) {
    Write-Host "Previewing rows that would be appended to $TableName (no write will occur)."
    $response = Invoke-KustoRequest -Endpoint 'query' -Command $selectRows -AccessToken $accessToken
    $primary = $response.Tables[0]
    $columns = $primary.Columns.ColumnName
    $rows = @($primary.Rows | ForEach-Object {
        $values = $_
        $record = [ordered]@{}
        for ($i = 0; $i -lt $columns.Count; $i++) {
            $record[$columns[$i]] = $values[$i]
        }
        return [pscustomobject]$record
    })
    $rows | Format-Table -AutoSize | Out-String -Width 250 | Write-Host
    Write-Host "$($rows.Count) row(s) would be appended."
    return
}

if (!$PSCmdlet.ShouldProcess($TableName, 'Append live test cleanup metrics')) {
    return
}

Write-Host "Appending live test cleanup metrics to $Database/$TableName (lookback ${LookbackDays}d)..."
$response = Invoke-KustoRequest -Endpoint 'mgmt' -Command ".set-or-append $TableName <|`n$selectRows" -AccessToken $accessToken

# .set-or-append returns one row per extent created; no rows means there was nothing new to add.
$extentCount = @($response.Tables[0].Rows).Count
Write-Host "Append completed. Extents created: $extentCount"

return
