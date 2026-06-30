# Threats Interfaces

Cross-domain interfaces for threat read models and audio hooks.

Current contents:

```text
IThreatArrivalSource      - ThreatMovementSystem -> ThreatArrivalSystem drain contract
IBallisticSnapshotSource  - ThreatMovementSystem -> BallisticDefenseSystem snapshot contract
IThreatAudioService       - Threat audio playback facade
IThreatTargetReader       - ThreatTargetSystem -> UI target snapshot reader
```

Keep this folder small and capability-oriented. Add a new interface only when a distinct consumer needs a stable contract that should not depend on the concrete threat-domain system.
