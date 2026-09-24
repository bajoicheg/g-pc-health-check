#!/usr/bin/env python3
"""Check installed package integrity and validate templates with their actual parsers."""
from pathlib import Path
import json
import re
import sys

sys.dont_write_bytecode = True
from contracts import ContractError, load_yaml, semver
from validate_adapter import ADAPTER_SCHEMA, validate_adapter
from validate_checkpoint import validate_checkpoint
from validate_checkpoint_24 import validate_checkpoint_24
from run_checks import load_plan
from operation_intent import validate_intent
from execution_lease import validate as validate_lease
from execution_lease_v2 import validate as validate_lease_v2
from execution_continuity import evaluate as evaluate_continuity
from resume_capsule import validate as validate_resume_capsule
from budget import validate_ledger
from recovery import validate_wait_state, decide_recovery
from watchdog_health import assess as assess_watchdog_health

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    'SKILL.md', 'VERSION', 'manifest.json', 'agents/openai.yaml',
    'references/runtime-routing-and-subagents.md', 'references/task-lifecycle.md',
    'references/validation-compute-and-ci.md', 'references/codex-compute.md',
    'references/progress-and-checkpoints.md', 'references/watchdog-recovery-and-migration.md',
    'references/release-management.md', 'references/adapter-schema.md',
    'references/policy-compatibility.md', 'references/command-evidence.md',
    'references/external-operations.md', 'scripts/requirements.txt',
    'scripts/contracts.py', 'scripts/validate_adapter.py', 'scripts/validate_checkpoint.py',
    'scripts/run_checks.py', 'scripts/validate_evidence.py', 'scripts/operation_intent.py',
    'templates/development-cycle.yaml', 'templates/work-status.md',
    'templates/check-plan.json', 'templates/operation-intent.json',
    'templates/watchdog-prompt.md', 'templates/AGENTS.snippet.md',
    'tests/pressure-scenarios.md', 'tests/test_adapter.py', 'tests/test_checkpoint.py',
    'tests/test_evidence.py', 'tests/test_operation_intent.py',
    'references/orchestration-controls.md', 'references/execution-ownership.md',
    'references/bounded-recovery.md', 'references/budget-ledger.md',
    'scripts/execution_lease.py', 'scripts/git_lease_store.py',
    'scripts/recovery.py', 'scripts/budget.py',
    'templates/execution-lease.json', 'templates/external-wait.json',
    'templates/recovery-snapshot.json', 'templates/budget-ledger.json',
    'tests/test_execution_lease.py', 'tests/test_recovery.py',
    'tests/test_budget.py', 'tests/test_orchestration_policy.py',
    'tests/test_v233_guidance.py',
    'scripts/watchdog_health.py', 'templates/watchdog-health.json',
    'tests/test_watchdog_health.py',
    'tests/test_v238_guidance.py',
    'references/control-plane-v2.4.md',
    'scripts/execution_lease_v2.py', 'scripts/execution_continuity.py',
    'scripts/resume_capsule.py', 'scripts/validate_checkpoint_24.py',
    'templates/execution-lease-v2.json', 'templates/execution-continuity.json',
    'templates/resume-capsule.json', 'templates/work-status-v4.md',
    'tests/test_execution_lease_v2.py', 'tests/test_execution_continuity.py',
    'tests/test_resume_capsule.py', 'tests/test_checkpoint_v4.py',
    'tests/test_v240_guidance.py',
]


def validate():
    missing = [name for name in REQUIRED if not (ROOT / name).is_file()]
    if missing:
        raise ContractError('missing package files: ' + ', '.join(missing))
    version = (ROOT / 'VERSION').read_text().strip()
    semver(version)
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    if manifest.get('version') != version or manifest.get('schema') != ADAPTER_SCHEMA or manifest.get('entrypoint') != 'SKILL.md':
        raise ContractError('manifest version/schema/entrypoint mismatch')
    metadata = load_yaml(ROOT / 'agents/openai.yaml')
    if str(metadata.get('version')) != version:
        raise ContractError('agent metadata version mismatch')
    skill = (ROOT / 'SKILL.md').read_text()
    frontmatter = load_yaml(ROOT / 'SKILL.md', frontmatter=True)
    if frontmatter.get('name') != 'continuous-development-cycle':
        raise ContractError('wrong skill name')
    description = frontmatter.get('description', '')
    if not description.startswith('Use when') or len(description) > 500:
        raise ContractError('description must start with Use when and fit 500 characters')
    if f'# Continuous Development Cycle v{".".join(version.split(".")[:2])}' not in skill:
        raise ContractError('skill heading version mismatch')
    for term in ('ordinary ChatGPT chat', 'ChatGPT Work', 'Codex used as the orchestration environment',
                 'minimum sufficient effort', 'COMPUTE_ONLY', 'concurrency guard',
                 'runtime/tool limit', 'release-candidate SHA', 'policy-compatibility.md',
                 'command-evidence.md', 'external-operations.md'):
        if term.lower() not in skill.lower():
            raise ContractError('skill contract missing: ' + term)
    adapter = load_yaml(ROOT / 'templates/development-cycle.yaml')
    validate_adapter(adapter, version)
    validate_checkpoint(load_yaml(ROOT / 'templates/work-status.md', frontmatter=True), adapter)
    validate_checkpoint_24(load_yaml(ROOT / 'templates/work-status-v4.md', frontmatter=True), adapter)
    load_plan(ROOT / 'templates/check-plan.json')
    validate_intent(json.loads((ROOT / 'templates/operation-intent.json').read_text()))
    validate_lease(json.loads((ROOT / 'templates/execution-lease.json').read_text()))
    validate_lease_v2(json.loads((ROOT / 'templates/execution-lease-v2.json').read_text()))
    validate_resume_capsule(json.loads((ROOT / 'templates/resume-capsule.json').read_text()))
    continuity = evaluate_continuity(json.loads((ROOT / 'templates/execution-continuity.json').read_text()))
    if not continuity['allowed']:
        raise ContractError('invalid CDC 2.4 execution-continuity template: ' + continuity['reason'])
    validate_ledger(json.loads((ROOT / 'templates/budget-ledger.json').read_text()))
    validate_wait_state(json.loads((ROOT / 'templates/external-wait.json').read_text()))
    snapshot = json.loads((ROOT / 'templates/recovery-snapshot.json').read_text())
    probe = dict(snapshot, schema='recovery-probe/v1', complete=True, source_valid=True)
    recovery = decide_recovery(snapshot, probe, snapshot['observed_at_utc'])
    if not recovery['fast_path']:
        raise ContractError('invalid recovery template: ' + '; '.join(recovery['reasons']))
    health = assess_watchdog_health(json.loads((ROOT / 'templates/watchdog-health.json').read_text()))
    if health['overall'] != 'HEALTHY' or any(health[name] for name in ('authorizes_takeover', 'authorizes_external_start', 'authorizes_product_write')):
        raise ContractError('invalid watchdog health template/authority contract')
    for path in ROOT.rglob('*.md'):
        content = path.read_text()
        # Only portable package paths; repository paths in examples remain project-specific inputs.
        for relative in re.findall(r'`((?:references|templates|scripts)/[A-Za-z0-9_.-]+)`', content):
            if not (ROOT / relative).is_file():
                raise ContractError(f'broken package reference in {path.name}: {relative}')
    banned = ['bajo' + 'icheg', 'g-' + 'ad-control', 'Grad' + 'ient', 'Гра' + 'диент']
    for path in ROOT.rglob('*'):
        if path.is_file() and path.suffix in {'.md', '.py', '.yaml', '.json', '.txt'}:
            content = path.read_text()
            for literal in banned:
                if literal.lower() in content.lower():
                    raise ContractError(f'project-specific literal in {path.relative_to(ROOT)}')
    return version


def main():
    try:
        version = validate()
    except (ContractError, OSError, UnicodeError, ValueError) as exc:
        print(f'FAIL: {exc}')
        return 1
    print(f'PASS: continuous-development-cycle {version}, {len(REQUIRED)} files and parsed templates')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
