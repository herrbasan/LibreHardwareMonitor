# Copilot Instructions

## Repository Purpose

This is a fork of [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) maintained for integration with the **LibreHardwareMonitor_NativeNodeIntegration** project.

## Branch Strategy

| Branch | Purpose |
|--------|---------|
| `master` | Keep clean and synced with upstream. Do not add custom features here. |
| `node-integration-features` | Contains custom features for LibreHardwareMonitor_NativeNodeIntegration. |

## Workflow

1. **Syncing with upstream:**
   ```bash
   git checkout master
   git fetch upstream
   git merge upstream/master
   git push origin master
   ```

2. **Updating feature branch with upstream changes:**
   ```bash
   git checkout node-integration-features
   git rebase master
   git push origin node-integration-features --force-with-lease
   ```

## Custom Features (node-integration-features)

- **DIMM Detection Toggle** - Option to enable/disable DIMM memory detection
- **Physical Network Adapter Filter** - Filter to show only physical network adapters

## Remotes

- `origin` → https://github.com/herrbasan/LibreHardwareMonitor.git (this fork)
- `upstream` → https://github.com/LibreHardwareMonitor/LibreHardwareMonitor.git (original repo)
