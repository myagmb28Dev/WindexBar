# Exact Reset Credit Tracking Design

## Goal

Replace WindexBar's locally estimated banked reset-credit expiration tracking with the exact reset-credit detail rows returned by Codex app-server 0.144.1 and later.

## Scope

- Read `rateLimitResetCredits.credits` from `account/rateLimits/read`.
- Preserve the authoritative `availableCount` returned by app-server.
- Display exact expiration timestamps when a detail row contains `expiresAt`.
- Display the number of credits without details as `Expiration unavailable` / `만료 정보 미제공`.
- Remove every local first-seen, 30-day estimation, legacy-credit repair, and reset-credit state persistence path.
- Update tests and README wording to describe the exact-data behavior.

The existing `%APPDATA%\WindexBar\codex-reset-credits.json` file becomes inert. WindexBar will no longer read or write it, but this change will not delete user files during upgrade.

## Protocol and Domain Model

`RpcRateLimitResetCreditsSummary` gains a nullable `Credits` collection. Each `RpcRateLimitResetCredit` maps the app-server fields:

- `id`
- `grantedAt`
- `expiresAt`
- `resetType`
- `status`
- `title`
- `description`

The core model replaces `RateLimitResetCreditObservation` with `RateLimitResetCredit`. It stores the opaque ID, exact grant and expiration timestamps, backend status and type, and optional display metadata. `RateLimitResetCreditsSnapshot` continues to store `AvailableCount` and `UpdatedAt`, plus the returned detail rows.

`UnavailableExpirationCount` is derived as `max(0, AvailableCount - Credits.LongCount(credit => credit.ExpiresAt is not null))`. It represents credits whose expiration cannot be shown without guessing, including missing detail rows and returned rows whose `expiresAt` is `null`. The model will not synthesize rows or timestamps.

## Data Flow

1. `CodexRpcClient` deserializes `account/rateLimits/read` into the expanded RPC model.
2. `CodexUsageMapper` converts Unix seconds to `DateTimeOffset` values and maps valid detail rows into the core model.
3. Invalid required timestamps cause only that detail row to be omitted; the authoritative total remains available and the omitted row is counted as unavailable detail.
4. `UsageStore` assigns the provider result directly. It performs no reset-credit tracking or persistence.
5. `RateLimitResetCreditFormatter` formats exact expiration values and reports any missing detail count explicitly.

## Display Behavior

The summary keeps the authoritative held count. When exact detail exists, it calculates the first-expiry D-day from the actual earliest `expiresAt`, without estimated wording.

The detail view groups credits by exact expiration timestamp and displays local date and time, for example:

```text
2026-08-01 05:05 만료: 1개
```

If `credits` is `null`, empty, capped, or contains a row without `expiresAt`, WindexBar does not calculate a date. The unmatched count appears as:

```text
만료 정보 미제공: 1개
```

The English equivalent is `Expiration unavailable: 1`.

## Removed Components

- Delete `RateLimitResetCreditTracker`.
- Delete `RateLimitResetCreditState` and `IRateLimitResetCreditStateStore`.
- Delete `FileRateLimitResetCreditStateStore` and its JSON serialization registration.
- Remove the tracker dependency and `TrackResetCredits` post-processing from `UsageStore`.
- Remove tracker, repair, estimated-expiry, and in-memory state-store tests.
- Remove README statements describing best-effort estimates.

## Compatibility and Error Handling

Older Codex CLI versions that expose only `availableCount` deserialize with `Credits == null`. WindexBar still shows the authoritative count and marks every held credit as missing expiration detail.

`expiresAt` is nullable by protocol, so a non-expiring or unspecified credit is reported as unavailable rather than assigned a synthetic date. A backend detail-list cap is handled the same way through `UnavailableExpirationCount`.

No stale API details are cached. A refresh always reflects the latest app-server response.

## Testing

Implementation follows red-green-refactor cycles:

1. Add a failing RPC/mapper test for exact `grantedAt` and `expiresAt` conversion.
2. Add a failing mapper test for `credits: null`, capped detail rows, and invalid timestamps without estimation.
3. Add failing formatter tests for exact local date/time ordering and unavailable-detail wording in Korean and English.
4. Remove tracker-specific tests and add a `UsageStore` pass-through test proving snapshots are not locally mutated.
5. Run the complete core test project and solution build.

## Success Criteria

- No production source contains `EstimatedExpiresAt`, `FirstSeenAt`, `EstimatedLifetime`, `AddDays(30)`, or reset-credit state-store types.
- No WindexBar code reads or writes `codex-reset-credits.json`.
- Exact app-server timestamps appear in the reset-credit UI.
- Missing details are labeled as unavailable and never inferred.
- The complete test project and solution build pass.
