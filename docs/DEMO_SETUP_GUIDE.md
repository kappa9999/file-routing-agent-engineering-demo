# Demo And Setup Guide

This guide is designed for your engineering-firm demonstration meeting and first pilot deployment.

## Meeting Outcome Targets
- Show engineers the prompt workflow is simple and non-intrusive.
- Show managers the audit trail and conflict safety controls.
- Show IT the deployment model and policy controls are manageable.

## Pre-Meeting Preparation
1. Build and run tests:
```powershell
dotnet build FileRoutingAgent.slnx
dotnet test FileRoutingAgent.slnx --no-build
```
2. Install sample connector script:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-ProjectWiseConnectorSample.ps1 -Force
```
3. Run local smoke test:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\LocalSmokeTest.ps1
```

## Live Demo Flow (15-20 Minutes)
1. Open tray app and show menu controls.
2. Launch `Easy Setup Wizard (Recommended)` from tray menu and complete setup.
3. Save test PDF to `_Working` folder to trigger detection.
4. Choose `Move` with category `progress_print`.
5. Show routed file in official destination.
6. Create naming conflict and show keep-both/overwrite/cancel handling.
7. Open Diagnostics and show connector publish event row.
8. Open pending detections and demonstrate retry + dismiss.

## Talking Points For Engineering Leadership
- No silent overwrite policy prevents accidental print-set loss.
- CAD default is publish-copy to avoid reference breakage risk.
- Scanner backstop handles missed SMB watcher events.
- Full audit log supports QC and accountability.
- Connector path allows gradual ProjectWise integration without redesign.

## Talking Points For IT
- Windows-native `.NET 8` tray agent.
- No heavy central server required for MVP.
- Policy integrity check can enforce trusted config.
- Runtime data isolated under `%LOCALAPPDATA%\FileRoutingAgent`.

## First Pilot Rollout Checklist
1. Define candidate roots and official destination categories for 1 pilot project.
2. Install tray app on 3-5 users.
3. Keep prompt cooldown at 20 minutes for week one.
4. Review audit events daily and tune ignore patterns.
5. Enable connector profile after base routing is stable.
