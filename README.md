# Phinix Legacy Red Packet

<p align="center">
  English · <a href="./README.zh-CN.md">简体中文</a>
</p>

The official Phinix managed plugin port of the legacy Red Packet service (Package ID: `phinix.legacy.redpacket`), providing colony-to-colony item gift packets and lucky draws in RimWorld 1.6.

---

## Overview & Attribution

- **Purpose**: Allows players to pack in-game items, silver, and equipment into lucky or equal-share red packets distributed to other colonies via a relay service.
- **Attribution & Rights**: Based on the legacy Phinix red-packet mod. Original upstream authorship and license terms are preserved; this repository maintains the standalone managed plugin port for Phinix Rework.
- **Delivery Model**: Fully decoupled from the core Phinix Mod package. It installs and runs as an independent managed DLL plugin via the Phinix extension lifecycle.

---

## Installation & Prerequisites

### In-Game Installation (Recommended)

1. Open the Phinix window in RimWorld and navigate to the **Store** (`商店`) tab.
2. Locate **Red packets**, select the latest available Store version and click **Install** (`安装`).
3. **Restart RimWorld** for the newly installed plugin assemblies to take effect.

### Prerequisites

- **RimWorld 1.6**
- **Phinix Rework** (with Trade and Inventory core modules active)

---

## Relay Service Configuration

Red packet synchronization relies on a dedicated network relay endpoint:
- **Configuration Panel**: Access settings via **Phinix Settings** (`设置`) → **Red Packet Settings**.
- **Relay Parameters**: Enter the target relay service address and room key as instructed by your server administrator or community host.

> [!CAUTION]
> **Credential Security**: Never commit, share, or publish live relay access keys or server secrets in public documentation, GitHub issues, or pull requests. Third-party legacy relay services are community infrastructure and are not operated or guaranteed by the Phinix core team.

---

## Sending & Claiming Packets

1. **Creating a Red Packet**:
   - Open the **Red Packet** (`红包`) tab.
   - Select items from your colony's unified virtual inventory or map stockpile.
   - Set the number of shares and select distribution mode (**Lucky Draw / 拼手气** or **Equal Share / 普通等额**).
   - Confirm and dispatch the packet.
2. **Item Compatibility Scope**:
   - Standard items and multi-stack commodities (silver, components, medicine, food) are supported.
   - For complex stateful items (weapons with custom quality/infusions, biocoded gear, pawns), test with small quantities first to verify state integrity across the relay.
3. **Claiming Packets**:
   - When another colony dispatches a packet, a notification appears on the tab badge and event banner.
   - Click to claim your share. Claimed items are automatically transferred into your colony's virtual inventory for extraction.
4. **Expiration & Returns**:
   - Unclaimed packets return remaining items to the sender's inventory once their validity window expires.

---

## Verification Status & Safe Removal

- **Publication Status**: Admitted to the official Phinix Plugin Index (v1.0.0, PR #26).
- **Game Acceptance**: The user confirmed this local 1.0.2 game test passed. All 17 algorithm regressions and package preflight passed; individual third-party item components and relay configurations still require their own verification. See the [changelog](CHANGELOG.md).

### Safe Uninstallation

> [!WARNING]
> Before disabling or uninstalling this plugin, verify that no outbound packets are pending settlement and all expired returns have been retrieved.

1. Withdraw or allow any active red packets sent by your colony to expire.
2. Claim all pending return items into your colony stockpile.
3. In **Extension Manager** (`扩展管理`), disable or uninstall **Red packets**, then restart RimWorld.

---

## Build & Candidate Verification

Requires .NET 10 SDK, local Phinix-Rework source, and RimWorld 1.6 references:

```bash
# Verify source and references
python3 check-source.py

# Package candidate ZIP
python3 pack.py \
  --phinix-package <path-to-Phinix-Rework> \
  --game-references <path-to-RimWorld-Managed> \
  --packager <path-to-ManagedPackageTool.dll> \
  --output <path-to-output>/phinix-legacy-redpacket-1.0.2.zip
```

Never commit or redistribute RimWorld, host, or dependency assemblies in the candidate package.

## main/dev and official releases

Develop on dev; it triggers no Actions. Every accepted push to main reserves the next patch version in the configured major/minor series, builds against fixed client/Common source and private compile-only references, then publishes an official GitHub Release with the plugin ZIP, SHA256SUMS and source/build summary. Retrying the same commit reuses its reserved version; failed builds leave an unpublished draft for retry and never replace published bytes. Parallel pushes reserve distinct versions without canceling pending builds.

Maintainers configure BUILD_REFERENCES_TOKEN as a repository secret, with Contents:Read only on the private compile-reference repository named in ci/config.json. This is a maintainer CI setup; third-party authors supply their own licensed references. No game/host/Harmony DLL is uploaded in public artifacts. Assembly identity versions remain independently controlled by source; the automatically assigned package release version is passed to pack.py --version.

Merging into main means the author has accepted publication. A GitHub Release does not bypass Index admission/source-update policy or prove game acceptance. Test changes on dev before merging. Fixed host/reference inputs are maintained explicitly in ci/config.json. Do not overwrite released ZIPs or expose the reference token.

## Repair 1.0.2 (this local game acceptance passed)

The Store release is 1.0.1. Pushing dev does not run online Actions or update the Store. Physical selection validates intact source state before splitting, retains the original template and groups the UI by the same policy. Prefer one sufficient stack. Tick/reveal history, quest tags, quality and component state remain authoritative; the wire template is detached from the sender map.

Completion/expiry letters use a bounded deduplicated value-only queue. Missing game context defers delivery. World/account changes or shutdown discard old notices. A throwing letter hook is reported without repeating an uncertain letter side effect, holding a committed item event or awarding items again. Genuine protocol/item failures retain existing recovery behavior.

Run `dotnet run --project Tests/RedPacketRepairTests/RedPacketRepairTests.csproj --configuration Release`. The 17 cases use algorithm seams; the production plugin is separately compiled with game references. The user confirmed this local package passed game acceptance. Keep these regression scenarios: send 200 steel from three equivalent 75-item stacks (25 remain), receive all 200, repeat from inventory, reject different states without changing quantities, check final-claim and expiry counts/letters, and return to menu/switch saves without notification errors. Merging dev into main publishes the official ZIP while retaining dev for further work; Index source policy then discovers it.
