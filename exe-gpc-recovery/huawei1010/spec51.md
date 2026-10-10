# Independent SPEC review: Huawei BIOS discovery

Disposition: **SPEC_GREEN**, limited to the exact research/discovery ZIP and proposal contract. No blocking specification defect was found. This does not approve a product update, prove BIOS telemetry availability, or replace the subsequent QUALITY and Windows/Huawei gates.

Task `spec`; attempt `gpc-huawei-spec51-20261010`; reservation `9b0b874b-fd38-49cc-a2bd-a452040e84e7`. Parent supplied the durable running claim at coordination commit `528e9b2c9d62350b873a52e9552ac7033a9dc8e3`. This reviewer did not acquire or mutate the product lease, coordination state, Git refs, source, EXE or backend operations. CDC remains pinned by the parent to 2.12.1; no adoption or upgrade was performed.

First independently observed review timestamp: `2026-10-10T01:39:21Z`. Review result preparation finished at `2026-10-10T01:41:24Z`. The exact agent/provider start time was not observed and is unknown; these are local observation timestamps, not fabricated launch timestamps.

## Exact bindings and checks

| Item | SHA-256 / identity |
| --- | --- |
| ZIP | `a76d2c0f1d0d9d59fd569ef4dd0b3a7d848ca2af74ca8ad79c9bf97d01c21539` |
| Engineering patch | `edf2b7f28578efc5284490f28dad42a31f2b410ff82b5618367e2f02fabcb693` |
| Probe PS1 | `3fa0a335dcb4d339078c58e49960a02e5115fbb2d73941fd3356abf3e27ddac4` |
| Exact original source | `67fe19905db6f8ec4796e688f797f0a916a1d0b04ccb5a4fa98421693a19223d` |
| Proposed source bytes | `7ff5c203a13228561395a6a9b0c2570c7521f55a5e9a6a6dcad416adfe1f45bb` |
| Candidate | `68da62237ecfa5377fcc31c5641b3baaebea39a9` |

Independently checked all seven archive members against the unpacked read-only files and all six hashes recorded in FILES.sha256. Original source was read using `git show` at the exact candidate, never from the mutable working tree. Removing the single `/v` argument in memory produces the contract's proposed source hash. The reviewer did not apply the patch or create a product candidate.

## Findings

1. **HIGH, existing source defect, accurately documented; not a ZIP blocker.** Exact68da calls `bcdedit /enum FIRMWARE /v`, while `ParseFirmwareEnumeration` locates its manager only through `{fwbootmgr}`. Microsoft documents that `/v` prints full identifiers instead of well-known names. The report correctly identifies a deterministic command/parser mismatch and labels removing `/v` as an unapplied proposal. An elevated query alone does not resolve this source defect. Compiler, runtime and acceptance evidence for a new product candidate remain absent.
2. **INFO, read-only and evidence boundaries satisfied by source inspection.** The probe reads CIM properties and class metadata, registry Secure Boot status, BCD enumeration output-format booleans, and bounded PC Manager file metadata/hashes. It does not invoke OEM methods, enumerate opcodes, load OEM DLL exports, request elevation, change policy, write BIOS/BCD, or export raw SMBIOS, machine/user names, serials, UUIDs, passwords or boot-path lists. BCD is conditional on an already elevated process. Output writes are the diagnostic evidence files; execution of the probe was not performed by this reviewer.
3. **INFO, absence and uncertainty are preserved.** Type24 absence, malformed/ambiguous tables, unavailable data, not-implemented and unknown statuses are separate. Failures preserve exception type, HResult and CIM/native code without exception messages. An absent OEM class is represented as ReadFailed with its CIM code, rather than a positive capability claim; consumers must examine that code to distinguish absence from access errors. File absence is explicitly confined to checked locations. BCD never sets ExternalBootDisabled to true or false, and successful enumeration is not presented as proof of USB/PXE or one-time-boot policy.
4. **INFO, OEM API not established.** The primary Huawei-WMI author lists GetBiosInfo and GetDeviceBiosSwitchStatus, with blank opcodes and without a usable BIOS/password GET parameter contract. The cited kernel source defines the Huawei GUID and battery/Fn/mic commands; no BIOS/password implementation was found in the cited revision. The research properly identifies reverse engineering and avoids claiming an official Huawei SDK or model-level support.
5. **INFO, validation disclosure is accurate.** README, research and contract explicitly state Windows runtime, physical Huawei runtime, embedded PowerShell selftests and product integration are NOT_RUN. The archive's tree-sitter preparation result is syntax evidence only. This Linux review ran no PowerShell, Windows, hardware, compiler, UI or selftest validation.

## Primary source checks

- Microsoft BCDEdit /enum: administrative view privileges and `/v` identifier contract, https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--enum
- Microsoft Win32_ComputerSystem: AdminPasswordStatus and PowerOnPasswordStatus map to SMBIOS Type24 and share the documented 0/1/2/3 meanings, https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-computersystem
- Huawei model mapping: MCLF maps to MateBook D 16 SE 2024 12th Gen Core, https://consumer.huawei.com/en/support/content/en-us15870847/
- Huawei distinguishes BIOS administrator and startup passwords, https://consumer.huawei.com/en/support/content/en-us00694834/
- Primary reverse engineering author, https://github.com/baldandbrave/Huawei-WMI
- Exact kernel source revision, https://android.googlesource.com/kernel/common/+/9c39d5ab450f7181775957000f4aff33bfef9f7b/drivers/platform/x86/huawei-wmi.c

## Limits and next disposition

SPEC acceptance covers the artifact's bounded discovery design and accurate proposal/report claims only. Original user HTML observations were supplied as task context and were not independently checked against an original HTML file in this review. No unknown BIOS/password value may be converted to false from these findings. Real elevated/non-elevated Huawei results remain necessary, and known output-format behavior must be verified on Windows before any product candidate is accepted. The guarded 7aa source/original CI6 state was neither changed nor bypassed. Parent integration and independent QUALITY remain outstanding.
