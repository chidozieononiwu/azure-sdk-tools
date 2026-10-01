# Live test resource cleanup dashboard

Tracks how many resource groups exist in each live test subscription over time, and how effectively
the cleanup pipeline is removing them.

## How the data flows

```
live-test-resource-cleanup.ps1          emits a CLEANUP_METRICS log line per subscription
  -> Azure DevOps build logs            'automation - live-test-resource-cleanup' (definition 1357)
  -> pipeline-witness                   ingests every log line into BuildLogLine
  -> Update-LiveTestCleanupMetrics.ps1  materializes counts into LiveTestResourceGroupSnapshot
  -> ADX dashboard                      queries the small snapshot table
```

### Why the materialization step exists

`BuildLogLine` holds every line of every build log in the org, so it is far too slow to back a
dashboard. Measured against a one day window the extraction query takes about 4 seconds; a 3 day
window takes minutes; and 10+ day windows time out even with a 15 minute server timeout, because
only recent data is in the hot cache.

`LiveTestResourceGroupSnapshot` holds roughly 36 rows per day (6 subscriptions x 6 runs), so
dashboard queries over months of history return instantly.

## Components

| Component | Location |
| --- | --- |
| Metrics emitted by the cleanup script | [`eng/scripts/live-test-resource-cleanup.ps1`](../eng/scripts/live-test-resource-cleanup.ps1) (`WriteCleanupMetrics`) |
| Target Kusto table | [`LiveTestResourceGroupSnapshot.kql`](../tools/pipeline-witness/infrastructure/kusto/tables/Dashboards/LiveTestResourceGroupSnapshot.kql) |
| Materialization script | [`eng/scripts/Update-LiveTestCleanupMetrics.ps1`](../eng/scripts/Update-LiveTestCleanupMetrics.ps1) |
| Scheduled pipeline | [`eng/pipelines/live-test-cleanup-metrics.yml`](../eng/pipelines/live-test-cleanup-metrics.yml) |

Cluster: `https://azsdkengsys.westus2.kusto.windows.net`, database `Pipelines`.

### The CLEANUP_METRICS contract

`WriteCleanupMetrics` writes a single line of the form:

```
CLEANUP_METRICS {"timestamp":"...","subscriptionId":"...","totalGroups":42,...}
```

The property names in that JSON must stay in sync with the `project` clause in
`Update-LiveTestCleanupMetrics.ps1` and with the columns in `LiveTestResourceGroupSnapshot.kql`.
If you add a count, update all three.

The materialization query also has a fallback that scrapes the older `Total Resource Groups:` and
`Total Resource Groups To Delete:` lines, so history from before `CLEANUP_METRICS` was added is
still captured (with the extra columns left null and `Source == "logscrape"`).

## Deploying

1. Merge the table definition. pipeline-witness CI picks up anything under
   `tools/pipeline-witness/infrastructure/kusto/tables/` automatically and deploys it with the
   `Azure SDK Engineering System` service connection.
2. Merge the pipeline and create it in Azure DevOps as `automation - live-test-cleanup-metrics`,
   pointed at `eng/pipelines/live-test-cleanup-metrics.yml`.
3. Let it run once, or run it manually, then build the dashboard below.

To preview without writing anything:

```powershell
./eng/scripts/Update-LiveTestCleanupMetrics.ps1 -WhatIf -LookbackDays 1
```

The script authenticates with `Get-AzAccessToken`, so you need an `Az` context in the corporate
tenant (`72f988bf-86f1-41af-91ab-2d7cd011db47`) when running it locally.

### Backfill

`BuildLogLine` retains a limited window. To backfill, run the script repeatedly with a small
lookback rather than one large one, because large windows time out:

```powershell
# Walk backwards a day at a time. The append is idempotent, so overlap is harmless.
1..7 | ForEach-Object { ./eng/scripts/Update-LiveTestCleanupMetrics.ps1 -LookbackDays $_ }
```

## Building the dashboard

Azure Data Explorer dashboard definitions are a versioned JSON format that cannot be validated
outside the portal, so the tiles are documented as queries here rather than checked in as a JSON
blob that would silently rot. Creating the dashboard is a few minutes of clicking.

1. Go to <https://dataexplorer.azure.com/dashboards> and choose **New dashboard**, named
   `Live Test Resource Cleanup`.
2. Add a data source: cluster `https://azsdkengsys.westus2.kusto.windows.net`, database `Pipelines`.
3. Add each tile below. All queries use the dashboard's built-in `_startTime` / `_endTime`
   time range parameters, so the range picker drives every tile.

### Tile 1 — Resource groups over time (line chart)

The headline tile: is each subscription trending up or holding steady?

```kusto
LiveTestResourceGroupSnapshot
| where Timestamp between (_startTime .. _endTime)
| summarize TotalGroups = max(TotalGroups) by bin(Timestamp, 4h), JobName
| render timechart with (title="Resource groups by subscription")
```

### Tile 2 — Groups queued for deletion per run (line chart)

How much work cleanup is actually doing. A flat line at zero next to a high Tile 1 value means
cleanup is not keeping up.

```kusto
LiveTestResourceGroupSnapshot
| where Timestamp between (_startTime .. _endTime)
| summarize ToDelete = max(ToDelete) by bin(Timestamp, 4h), JobName
| render timechart with (title="Groups queued for deletion per run")
```

### Tile 3 — Current state (table)

Latest snapshot per subscription, with headroom against Azure's limit of 980 resource groups per
subscription. That limit is hard and cannot be raised via support request.

```kusto
LiveTestResourceGroupSnapshot
| where Timestamp between (_startTime .. _endTime)
| summarize arg_max(Timestamp, TotalGroups, ToDelete, ToClean, ToDeleteSoon, ToDeleteLater) by JobName
| extend PercentOfCap = round(100.0 * TotalGroups / 980, 1)
| extend Status = case(TotalGroups >= 980, "AT CAP", PercentOfCap >= 80, "Near cap", "Healthy")
| project JobName, Status, TotalGroups, PercentOfCap, ToDelete, ToClean, ToDeleteSoon, ToDeleteLater, LastSeen = Timestamp
| sort by PercentOfCap desc
```

Use **Visual formatting** on the tile to add conditional colouring on `PercentOfCap`.

### Tile 4 — Stalled cleanup (table)

Subscriptions holding resource groups where no run in the window deleted anything. This is the
alerting tile.

```kusto
LiveTestResourceGroupSnapshot
| where Timestamp between (_startTime .. _endTime)
| summarize Runs = count(), Deleted = sum(ToDelete), MaxGroups = max(TotalGroups), LastSeen = max(Timestamp) by JobName
| where Deleted == 0 and MaxGroups > 0
| project JobName, Runs, MaxGroups, Deleted, LastSeen
| sort by MaxGroups desc
```

### Tile 5 — Net change over the window (bar chart)

Whether the backlog grew or shrank. Positive means groups are accumulating faster than they are
removed.

```kusto
LiveTestResourceGroupSnapshot
| where Timestamp between (_startTime .. _endTime)
| summarize (FirstSeen, Start) = arg_min(Timestamp, TotalGroups),
            (LastSeen, End) = arg_max(Timestamp, TotalGroups) by JobName
| project JobName, Start, End, Change = End - Start, FirstSeen, LastSeen
| sort by Change desc
```

## Known findings

- **ACS is pinned at the cap.** It reports exactly 980 resource groups on every run with 0 queued
  for deletion. 980 is Azure's per-subscription resource group limit, so the subscription is full
  and cleanup is not reclaiming anything. Tiles 3 and 4 both surface this.
- **TME churns healthily**, sitting around 350 groups and deleting 100-150 per run.
- Log scraped history (`Source == "logscrape"`) occasionally disagrees with itself, for example one
  TME run reporting a total of 3 alongside 73 queued for deletion, and one Cosmos run logging no
  total at all. This is exactly why the structured `CLEANUP_METRICS` line was added; prefer
  `Source == "structured"` rows when accuracy matters.

## Related

- [Engineering system resource management](./engsys_resource_management.md)
