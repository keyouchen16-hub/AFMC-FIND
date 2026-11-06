# AFMC FIND 0.8.16 — approved workbench and evidence-based startup

Owner: Codex. Branch: codex/initial-development. No commit/push. User explicitly approved implementing the reviewed UI and continuing player repairs, with validated automatic installation.

## Implemented

- Source page combines inline URL recognition, verified confirmation, added sources and adapted catalog. Public/account and content filters, category labels, per-row enable and context actions. Source editing opens configuration without a second directory. Changed URL cancels/invalidate previous evidence; recognition alone never saves. Connection status is transient, not serialized.
- Detail download/copy actions share equal width; provenance, original-page and resource-information actions live directly in the detail scroll. Existing native player retained.
- Settings includes both existing marks, public feedback email, copy action and Find the You. Existing preferences and reduced-motion behavior preserved. Native compact switches replace default system boxes.
- Background playback verification can start after six eligible first-batch candidates, without waiting for every provider. Results keep their position when health changes; an explicit update-order action applies new evidence. Name/intent matching continues ahead of playback evidence.
- Payload progress may extend the initial 60-second race deadline up to 120 seconds. Existing per-attempt no-progress timeout remains. No percentage-based startup threshold. Only decoded frames and advancing clock count as playable. Received unverified payload now has its own label.
- During native drag the independent control window remains nonpainting and restores in position; the native STATIC video child now uses a black brush, preventing a white idle rectangle when the overlay is transparent. No driver/system rendering settings changed.

## Evidence collected

- `evidence/v0816/movie-before.json`: both public tracker samples failed within the old 60-second race; 4 MiB / 2.73 MiB startup pieces still unverified despite increasing payload.
- `movie-after.json` / `movie-after.log`: one advertised movie sample decoded after 69.4 seconds; the other obtained verified data. This is startup evidence only, not full-film playback, content identity, repeatability, or a speed guarantee. Network conditions vary; not a controlled bandwidth A/B experiment.
- Fresh official Sintel torrent fetch failed with TLS EOF (`public-before.log`). The already-saved official torrent was explicitly used thereafter. Fixed 50-second diagnostic timed out with verified data (`public-cached.log`); actual adaptive policy decoded video (`public-adaptive.log`). Fresh remote fetch is not claimed successful.
- Whole-window pointer tests include straight and reversing paths, idle and playing. `player-drag/v0816-input-diagnostic/drag.json` has four passes: no white frames, control layer restored, playback clock advanced, no sampled desktop residue 400 ms after release. Intermediate moving-screen capture differs from asynchronously updated window geometry; max mismatch is not zero and is not a proof of zero visible transient ghosting. Two earlier input runs did not move the window and are preserved as failed acceptance, not passes.
- Isolated workbench exercises real WPF URL detect/save/invalidate, access/category filters, equal detail actions, settings email and both logos. No live private source config is used for fixture mutations.

## Release status

Installed and running: **0.8.16**, process30340 at acceptance. Final backup `%LOCALAPPDATA%/AFMC/Find-install-backups/20261105-230426-488`; original0.8.15 rollback remains in `20261105-230044-143`. Both replacements exited the preceding app normally, no forced close. Runtime manifests, licensing, package and installer staged hashes passed.

Applicable regression modules passed across the recorded final runs: search40, grouping42, display19, motion17, browser18, discovery33/deep13/UI5, glass1, Jackett15, core59, expanded26, reliable16, media20, PirateBay17, FOSS13, nativeUI18, quality32, integrity46, workbench91, tray/settings and playback96. The first release pipeline stopped at screenshot foreground validation; the retry exposed an intermittent MonoTorrent fixture magnet stall. Failures are retained. Screenshot validation now accepts only the exact owned LibVLC overlay HWND with matching bounds/process; isolated seeder metadata and prior-peer disconnect checks preceded the96-check pass. This is not proof that the historical library stall's complete root cause was resolved. Default installed libtorrent is tested independently below.

Installed checks:
- Read actual650-source catalog within the new page. Settings scroll/footer/feedback and cancellation passed. Real ubuntu search, selection, equal actions and direct provenance passed before the final badge-only wording update; final installed source/settings checks repeated. Initial main/source captures and one shortcut retry were denied foreground activation and remain **unverified**, not passes. Actual catalog/settings/selected-results captures were obtained in the first run; no injected sample rows.
- Final “少林足球”: first batch883ms; complete9175ms;128 resource groups /146 versions; first9visible titles match. Candidate App/Core/worker hashes and four live profile hashes agree (`installed-search.json`). Timings are a single observation, not a fixed performance guarantee.
- Installed default-engine direct link and magnet playback passed, magnet clock observed at11.94seconds with generated fixture seeded at512KiB/s; observed30.2% is an instant sample, **not a startup threshold**. Pause/stop, physical pointer drag idle/playing, fullscreen/restore, cleanup and3successive player closes preserve process30340.
- Final-state report: responsive; no AFMC app1000/1026events since final process start; playback and health cache folders empty. Live sources/preferences plus their `.bak` files and rollback profile hashes match the baseline.

Evidence: `docs/evidence/v0816/` and `docs/evidence/player-drag/v0816-input-diagnostic/`. No commits, pushes or user credential changes.

## Limits

Public BT availability and throughput remain variable. A verified startup is not proof of sustained throughput, complete download, correct content, or every future attempt. Detection is bounded and does not test hundreds of rows at once. Current monitor/DPI only; multi-monitor transitions and all refresh rates remain untested. No claim that all user-reported ghosting is eliminated.
