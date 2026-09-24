# CDC 2.3.9 execution continuity scenarios

## RED

Input:

```
runnable_next_action=true
blocker=false
external_wait=false
actions:
- status_read
- health_check
- report_only
```

Expected:

```
FAIL primitive_only_completion
```

## GREEN

Input:

```
runnable_next_action=true
blocker=false
external_wait=false
actions:
- status_read
- implementation_change
- checkpoint_update
```

Expected:

```
CDC_2_3_9_COMPLETION_GATE_GREEN
```
