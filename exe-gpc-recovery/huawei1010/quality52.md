# Independent QUALITY52 review — QUALITY_RED

Scope: exact read-only discovery artifact and unapplied engineering proposal, after reading genuine SPEC51_GREEN. No product source, executable, Git, coordination, backend, or delegation mutations. CDC remains pinned 2.12.1. This is static quality review, not Windows/Huawei acceptance.

## Exact reviewed payload

- ZIP SHA-256: `a76d2c0f1d0d9d59fd569ef4dd0b3a7d848ca2af74ca8ad79c9bf97d01c21539`
- PowerShell SHA-256: `3fa0a335dcb4d339078c58e49960a02e5115fbb2d73941fd3356abf3e27ddac4`
- Patch SHA-256: `edf2b7f28578efc5284490f28dad42a31f2b410ff82b5618367e2f02fabcb693`
- Original candidate: `68da62237ecfa5377fcc31c5641b3baaebea39a9`
- Original C# bytes SHA-256: `67fe19905db6f8ec4796e688f797f0a916a1d0b04ccb5a4fa98421693a19223d`
- Proposed C# bytes SHA-256: `7ff5c203a13228561395a6a9b0c2570c7521f55a5e9a6a6dcad416adfe1f45bb`

All seven archive members equal unpacked bytes; all six manifest entries verified. Exact original was read with git show; removing the sole /v argument in memory produces the declared proposed hash. No patch applied by this reviewer.

## Blocking findings, prioritized

**Q1 — P1: Required bounded output/file handling is incomplete.** `Read-HuaweiBiosEvidence.ps1:199–200` starts ReadToEndAsync on both BCD pipes, accumulating the complete output in memory; the 5-second process timeout and 1-second drain timeout do not impose a byte limit. At lines 239–245, three fixed filenames bound count only: FileVersionInfo and Get-FileHash read a user-selected file without any enforced size or duration limit; SizeBytes is merely exported. An oversized file named PCManager.exe in the supported custom directory can therefore consume arbitrary hashing time and delay all evidence publication. No runtime failure was induced; these are direct control-flow/API observations, and the BCD oversized-output case is an inference from the uncapped API contract. Before GREEN, cap captured BCD bytes while draining both pipes, record an explicit oversized-output status, and enforce a documented maximum file size before metadata/hash reads with a bounded read/deadline policy. Preserve unknown firmware conclusions on all truncation/timeout paths. Microsoft documents ReadToEndAsync as reading the remaining stream into a string: https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader.readtoendasync .

**Q2 — P2: The promised new-directory/no-overwrite invariant has a race.** The only directory absence check is line 110, before all CIM/BCD/file discovery. Line 254 later calls CreateDirectory, which reuses an existing directory. Concrete schedule: select an absent OutputDirectory; while discovery is running, another actor creates that directory and only evidence.sha256; the script then creates its new JSON (CreateNew succeeds) and line 264 truncates/overwrites the pre-existing checksum via WriteAllText. This contradicts the script/README promise that existing evidence is never overwritten. No race was executed on Windows. Create both output files using exclusive CreateNew semantics and establish ownership/freshness of the destination at write time; do not infer freshness from the earlier Test-Path. Microsoft confirms existing-directory reuse and overwrite behavior: https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createdirectory?view=netframework-4.8.1 and https://learn.microsoft.com/en-us/dotnet/api/system.io.file.writealltext?view=netframework-4.8.1 .

## Nonblocking observations

- PowerShell 5.1-facing APIs checked against Microsoft reference documentation: CimException exposes StatusCode and NativeErrorCode; CimMethodParameterDeclaration exposes Qualifiers; Get-CimInstance supports Property and OperationTimeoutSec. No concrete API incompatibility found. Documentation: https://learn.microsoft.com/en-us/dotnet/api/microsoft.management.infrastructure.cimexception ; https://learn.microsoft.com/en-us/dotnet/api/microsoft.management.infrastructure.cimmethodparameterdeclaration ; https://learn.microsoft.com/en-us/powershell/module/cimcmdlets/get-ciminstance?view=powershell-5.1 .
- The Type24 parser distinguishes NotPresent, MalformedTable and multiple Type24 records and leaves unproven password states unavailable. OEM lookup failures retain exception/CIM codes; missing OEM class is currently ReadFailed with a distinguishing code, not falsely ClassPresent. Lookup is metadata-only; no OEM method or DLL function invoked.
- The engineering patch is appropriate for the exact alias-only parser: /v emits full GUIDs, while the parser requires {fwbootmgr}. Removing /v addresses this mismatch. It is still an unapplied proposal requiring compiler, complete selftests and exact-SHA Windows/UI gates. Official contract: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--enum .
- No BIOS/BCD writes, automatic elevation, policy weakening, serial/user/computer-name/raw-SMBIOS export found. BCD /enum is read-only; ExternalBootDisabled remains null. Error messages are deliberately excluded.

## Verification and limits

PowerShell/pwsh/.NET are absent here. Windows runtime, real Huawei hardware, the 11 embedded PowerShell selftests, C# compilation and interactive UI are **NOT_RUN**. Parent-supplied tree-sitter syntax PASS is not a runtime PASS; this reviewer did not independently execute the tree-sitter parser. These limitations are accurately disclosed in the artifact and are not themselves the two blockers above. Product source7aa/original CI6 UNKNOWN guards remain untouched.

First actual clock observation: 2026-10-10T01:43:04Z. Report-preparation finish: 2026-10-10T01:44:41Z. Provider-agent start time unknown; no provider-duration claim. Hash coverage is only the unchanged payload above; later non-executing review/status appendices are outside this review.
