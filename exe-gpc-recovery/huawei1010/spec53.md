# Focused independent SPEC53 review — SPEC_RED

Scope: corrected Huawei discovery ZIP, not product/source acceptance. Attempt `gpc-huawei-spec53-20261010`; reservation `8cdd3b6f-1a4f-479c-8ac4-0b82e1a9e837`; parent-supplied running claim at coordination commit `4c29089c7055d31c628a47bf19b05847f11810d5`. CDC remains pinned 2.12.1. No product/source/Git/coordination/backend mutation or delegation was performed.

## Exact binding

| Item | SHA-256 |
| --- | --- |
| Corrected ZIP | `b08db66b9cf65fe9fcc7e98a59991881eab6daca3d69973c6cbf9b988d13ab97` |
| Corrected PS1 | `653cf45528781f6e8a492c0d2cccc32e5a60f13bf2b65752d12d9c82f0e29ca8` |
| Unchanged proposal patch | `edf2b7f28578efc5284490f28dad42a31f2b410ff82b5618367e2f02fabcb693` |
| Research | `34216541ff90d6e2120a64340604ea32911254652679107d0a383a8067bab0e1` |
| Patch contract | `0fff4764a47556202794fd421641a8ac567d9b13cd27391184306bf32e70c3b9` |

All seven ZIP members match unpacked files and all six manifest hashes match. Unchanged patch evidence from SPEC51 is reused only for its exact unchanged patch hash: the `/v` proposal remains unapplied, uncompiled and unvalidated on Windows; no new product candidate was reviewed.

## Blocking finding

**S53-1 — P1, network skip occurs after an unbounded network operation.** The main PC Manager loop calls `Test-Path -LiteralPath $path -PathType Leaf` before entering `Read-BoundedFileMetadata`. Only that function rejects UNC paths and network drives. Consequently a supplied UNC directory or mapped network drive reaches remote filesystem existence I/O before the promised skip. A slow/unreachable share can delay discovery outside the 5-second async hash clock; a nonexistent UNC file yields NotFoundInCheckedLocations instead of the claimed network-skip status. This is a direct source-order observation; no Windows timeout or remote I/O was executed. To meet the network-skip requirement, classify/reject the network path/drive before `Test-Path` or any file existence/open operation. Preserve an explicit SkippedNetworkPath/SkippedNetworkDrive result rather than treating the skipped source as local absence.

## Satisfied focused changes

- BCD captures each stream through 4096-character async buffers and appends at most 65536 characters per StringBuilder. Process execution and pipe collection share a 5000-ms stopwatch loop, with a separate up-to-1000-ms stop confirmation. Oversized output produces OutputLimitExceeded; incomplete execution/drain produces Timeout. Neither path reports successful enumeration or a proven external-boot restriction.
- A faulted async BCD read raises through GetResult and is caught as ReadFailed, rather than falling through to QuerySucceeded. Successful status requires process exit, both stream EOFs and exit code zero. This is static control-flow evidence only.
- PC Manager files open once with FileShare.Read; length over 64 MiB is rejected before hashing/version metadata. SHA-256 reads use 65536-byte buffers, a 5000-ms remaining deadline, and a cumulative 64-MiB cap; time/size limit statuses do not export a successful SHA-256. These are the intended hash-read limits, not proof that synchronous path lookup/open/version retrieval or Dispose has an overall five-second runtime bound.
- Both JSON and checksum use exclusive CreateNew FileStreams. The original QUALITY52 checksum-truncation schedule is blocked: an existing checksum is not overwritten. Existing output collisions fail rather than replacing prior files.
- The changed code retains metadata-only OEM discovery, zero OEM method/export/opcode invocation, no BIOS/BCD writes, automatic elevation or policy changes, no full SMBIOS/PII export, and Unknown/unavailable conclusions on failed evidence.

## Nonblocking remaining report precision

**S53-2 — P2 advisory, directory freshness claim exceeds the guarantee.** README still states that the script creates only a new directory. The early Test-Path and later Directory.CreateDirectory do not establish ownership of that directory at write time; an intervening directory containing unrelated files can be reused while both evidence files remain CreateNew. The concrete overwrite defect is corrected, but the remaining new-directory guarantee is not established. Either implement destination freshness/ownership or state the narrower exclusive-new-files guarantee accurately. This does not negate the observed CreateNew no-overwrite correction.

VERIFICATION correctly retains original QUALITY52_RED and corrected review pending. Its tree-sitter PASS is preparation-time syntax evidence, not Windows execution. Windows PowerShell, the 11 embedded tests, Windows/Huawei physical runtime, compilation and UI remain **NOT_RUN**. Physical performance/deadline behavior and cancellation/quiescence need real runtime validation. Original CI6/CI13 UNKNOWN guards and source7aa remain outside this review and untouched.

First genuine clock observation: `2026-10-10T01:49:45Z`. Report-preparation finish: `2026-10-10T01:51:20Z`. Provider/agent launch timestamp is unknown. No provider-duration claim is made. SPEC_RED is bound only to the exact corrected payload above; parent should correct S53-1 before an independent QUALITY acceptance claim.

Reused primary API references from the prior QUALITY review: https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createdirectory?view=netframework-4.8.1 . No new broad research was needed.
