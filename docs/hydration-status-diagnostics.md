# Hydration status diagnostics

`gvfs health --status` first requests a cached hydration summary from the mount process.
If that request fails, the command calculates a summary locally.
The diagnostic fields describe both attempts without a repository path or a raw pipe response.

## Calculation result

`HydrationSummaryDuration` contains one terminal record for each calculation.
It retains the existing duration fields and adds `SchemaVersion: 1`.
It records success, invalid counts, incomplete data, cancellation, and exceptions.
Success and cancellation use the Informational level.
Invalid counts, incomplete data, and exceptions use the Warning level.
This terminal record replaces the generic exception telemetry and the early-exit warning.

The six count fields contain numbers or JSON null.
A null value means that the calculation did not obtain that count.
A collected zero remains zero.
Partial counts remain available, but their source state is not `Complete`.

`IndexState`, `PlaceholderState`, `ModifiedPathsState`, and `FolderCountState` describe source completeness.
`CountsComplete` requires all four states to equal `Complete`.
`IsValid` requires complete sources and six nonnegative counts.
`ValidationFailures` independently records missing counts, negative counts, overflow, and hydrated counts that exceed their totals.
Its arithmetic uses 64-bit sums.
Excess counts and sums larger than a 32-bit integer do not make a complete summary invalid.
`FileCountDelta` and `FolderCountDelta` record the signed difference between the observed sum and its denominator.
`FileCountExcess` and `FolderCountExcess` record the positive part of each difference.
An unavailable input leaves its difference and excess null.

`FileCountSource` identifies the index header.
`FolderCountSource` identifies the projection or the index parser.
The folder denominator excludes the root and folders without projection-eligible entries.
Placeholder and modified-path records describe a different population.
They can include new paths, deletion records, overlap, and obsolete folders that still contain untracked files.
The summary does not intersect or deduplicate these populations.

## Estimated display

The CLI and the status hook use the same estimate:

| Observation | Component display |
| --- | --- |
| Positive denominator, sum within range | Integer percentage |
| Positive denominator, sum exceeds denominator | `100% (counts exceed current total)` |
| Both counts zero | `0%` |
| Positive sum, zero denominator | `n/a (no current denominator)` |

Every usable message starts with `Estimated hydration`.
The display cap does not change raw counts, denominators, wire values, or telemetry.
A component without a denominator does not hide the other component.
Incomplete sources, negative counts, cancellation, and exceptions remain unavailable results.
Count drift alone does not increment the circuit breaker.

## Calculation phases

`Phase` identifies the last started phase, which can already be complete when cancellation occurs between phases.
`CancellationRequested` distinguishes caller-requested cancellation from other cancellation exceptions.
Neither cancellation type increments the circuit breaker.
Each duration includes failed work in that phase.
An unattempted phase has a null duration.
`TreeCountMs` retains its existing name and measures the folder-count provider.
`FolderCountSource` identifies `Projection`, `IndexParser`, or `Unknown`.

The index fields record its observed length, version, signature validity, and header-read result.
An unavailable index total produces `IncompleteData`, not an arithmetic inconsistency.
They do not prove that all sources came from one atomic repository snapshot.
The placeholder fields distinguish partial, expanded, and possible-tombstone folders.

The modified-path fields describe the pipe attempt and the disk fallback separately.
The disk fields count add, delete, and other records.
These fields describe the existing reader.
They do not change its replay semantics or the recursive meaning of modified-folder entries.

## Command result and cache

The `health` completion record distinguishes `Status` mode from `Detailed` mode.
Status mode records the cache outcome, fallback decision, displayed result, and calculation identity.
Command `Outcome` uses `Success` when the command displays its result, or `Failed` when it reports an error.
`CalculationOutcome` describes the calculation separately.
Legacy cache responses leave `CalculationOutcome` unknown.
The nested `Cache` object retains parseable counts even when response validation fails.
Legacy mount responses have no cache diagnostics.
Their absent fields remain unknown.

`CommandId` identifies one health invocation.
`CalculationId` identifies one calculation and also travels with the cache response.
These fields appear explicitly in the JSON payload.
Activity identifiers in the local tracer are not sufficient for telemetry correlation.

The hydration pipe keeps its `S` and `NA` response headers.
An `S` response starts with the existing six comma-separated counts.
An optional extension follows those counts:

```text
D1,state,calculationId,ageMs,invalidated,countsComplete
```

An `NA` response can contain this extension without counts.
The state integers are fixed wire values:

| State | Integer |
| --- | --- |
| NotComputed | 0 |
| Available | 1 |
| ProviderUnavailable | 2 |
| FeatureDisabled | 3 |
| CircuitBreakerDisabled | 4 |
| InvalidCounts | 5 |
| IncompleteData | 6 |
| Cancelled | 7 |
| Failed | 8 |

The calculation identifier uses the GUID `N` format, or `-` when unavailable.
The age is nonnegative, or `-1` when unavailable.
The last two fields use `0` or `1`.
Older clients ignore the extension.
New clients accept responses without it.
New clients also accept complete nonnegative counts that exceed a denominator.
They reject a recognized extension that reports incomplete counts.
Older clients can reject excess counts safely.
A new client can use the fallback when an older mount rejects its own summary.

The mount publishes the summary and its diagnostic identity together.
Cache state describes the latest refresh or skip decision.
A skipped refresh can retain an earlier valid summary.
`Invalidated` compares the calculation's invalidation generation with the current generation.
It does not reject a cached summary or change the existing cache policy.
Heartbeat telemetry aggregates hydration requests and unavailable responses.
`HydrationStatus.CurrentCacheState` describes the cache at the heartbeat, not the state of earlier requests.

## Limits and privacy

The health cache lookup retains its 500 ms connection timeout and 1000 ms outer wait.
The hook retains its existing timeout and does not create a telemetry process or listener.
A timed-out health worker can finish later.
It owns its pipe and cannot write to the command's tracer or completion record.
The modified-path reader retains its existing connection and response-wait behavior.

Telemetry contains exception types and HRESULT values, not arbitrary exception messages or stacks.
Local warnings retain full exception details.
Hydration exceptions no longer produce generic Error-level telemetry or telemetry warnings with full exception text.
The structured result fields are identical in local logs and telemetry.
Raw details appear only in local warnings.

Telemetry delivery remains best-effort.
The listener can drop events when its queue is full or its collector is unavailable.
Process termination can also prevent a terminal record.
Mount telemetry cannot observe a hook connection failure that never reaches the mount.

This contract does not change count populations, disk-log replay, placeholder cleanup, or the circuit-breaker failure policy.
The disk fallback retains its pre-existing replay limitations.
The estimate does not make that reader an exact inventory.
