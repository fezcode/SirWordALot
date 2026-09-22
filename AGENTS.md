# Sir Word-a-lot working and release flows

## Scope and defaults

This repository is Sir Word-a-lot, a Windows vocabulary and thesaurus trainer
built with Avalonia/.NET 10. Preserve existing changes when working in a dirty
checkout. Build scripts live at the repository root; Forge is the sibling
`../Forge` project. The visual language is lifted from the sibling `../hammeros`
project, and the offline dictionary is exported from `../atlas.dict`.

`Core/` must never reference Avalonia. A test asserts this, and it is what keeps
scheduling, cloze matching, search and portability testable without a window.

## Commit messages

Use a normal commit title and body only. Do not add `Co-Authored-By` trailers or
AI/assistant attribution (including Claude or Codex) to commits or release notes.
For multiline messages or messages containing quotes, write a temporary UTF-8
message file and use `git commit -F <file>`. Check the exit code and verify the
resulting commit before tagging; PowerShell 5.1 silently splits inline quoted
messages and will produce a commit that is not the one you wrote.

## Verification

Four layers, none of which may drive the live desktop. Never use SendKeys,
synthetic clicks, or anything that steals focus; launching the app so the user
can use it is fine, driving their desktop is not.

1. `dotnet test tests/SirWordALot.Tests.csproj` — everything in `Core/`.
2. `dotnet run --project tools/Checks` — real Avalonia controls, headless.
3. `dotnet run --project tools/Preview -- dist/preview` — renders every view,
   the icon set and the README banner to PNG.
4. `dotnet run --project tools/Measure -- dist/preview` — reads those PNGs back
   and asserts the palette and chrome geometry in the pixels.

Two headless-rendering traps, both of which have already produced screenshots
that looked plausible and were wrong:

- `tools/Preview` must set `Motion.Enabled = false`. Motion animates opacity
  from zero through a `DispatcherTimer`; nothing pumps the dispatcher in a
  headless render, so leaving it on writes fully transparent screenshots that
  still pass any measurement sampling only window chrome.
- `tools/Preview` must also strip `Transitions` from button content presenters.
  Backgrounds cross-fade over 140ms and a frozen transition renders its
  *starting* value, so a button that just gained the selected class appears
  unselected and one that just lost it appears selected. This showed the
  sidebar with six highlighted entries at once while the running app was
  correct.

Anything animated is suspect in a headless render. When a preview disagrees
with the object model, check the live window before changing the app: the
measurement is more likely wrong than the program.

`Shell/Controls.axaml` is the single stylesheet. `App.axaml` and
`tools/Preview` both include it; never give the preview its own copy, or a
rendered screenshot can be styled differently from what ships.

## RELEASE workflow

Only an explicit request to **RELEASE** triggers the complete publishing flow.
Ordinary fixes, builds, and installer requests do not imply a version bump,
commit, push, tag, or GitHub release. When RELEASE is requested, perform these
steps in order and stop and report any failure before proceeding:

1. Run `./version.ps1 -Bump patch` by default, or `-Set x.y.z` for a requested
   version. Clarify conflicting or ambiguous version instructions. The script
   keeps `SirWordALot.csproj`, `Core/Product.cs`, the Forge app version, the two
   wizard strings and the registry Version synchronized. Run `./version.ps1`
   with no arguments to verify.
2. Run `./build.ps1` to build, run all four verification layers, publish the
   self-contained single-file host into `dist/SirWordALot`, and prove the
   published binary runs via `--selftest`. Do not skip tests for a release.
   Replacing this output closes a running copy of that build; report that and
   respect authorization already given.
3. Run `./installer.ps1 -SkipBuild` immediately afterward. Forge requires the
   sibling `../Forge/build/forge.exe` and `uninstall.exe`, produced by
   `gobake build` in Forge. Verify
   `dist/installer/SirWordALot-Setup-<version>.exe` and surface that exact path
   for testing. During RELEASE, launch the new Setup executable for the user to
   install and test; an installed copy stays old until Setup is run. Keep its
   option to launch the new app after installation enabled rather than
   restarting the old installed executable. Honor any requested test gate.
4. Review and commit the intended changes without attribution, using a message
   file. Verify the commit landed and record its hash before the next steps.
5. Push the commit to the configured remote and release branch (normally
   `origin main`). Inspect `git remote -v` and the branch first. Never infer a
   missing remote or force-push.
6. Create the matching `vX.Y.Z` tag on the verified commit, push it, and create
   a GitHub release with `gh release create vX.Y.Z`, attaching only the matching
   `dist/installer/SirWordALot-Setup-X.Y.Z.exe`. Use title
   `Sir Word-a-lot vX.Y.Z`; notes start with `## Sir Word-a-lot vX.Y.Z` followed
   by `### <feature>` sections and bullets, with no emoji. Supply notes through
   `--notes-file` to avoid PowerShell multiline argument splitting. Verify the
   published asset.

## Build and installer maintenance

- Keep `forge.toml` on the Mica wizard theme, using the app's icon and identity.
- Preserve user data under `%APPDATA%/fezcode/SirWordALot` unless the user
  chooses the installer's option to remove settings and data. That directory
  holds `library.db` and the rolling JSON backups, and losing it loses
  everything the user has studied.
- Fail on inconsistent versions, failed tests, checks or measurements, a missing
  payload, or a non-GUI Setup executable. Never report an old installer as a new
  success.
- Check GUI Forge process exit codes with `Start-Process -Wait -PassThru`; a
  GUI-subsystem binary does not propagate `$LASTEXITCODE` through the call
  operator. Quote arguments containing spaces.
- Keep `dist` tidy: it holds `SirWordALot/`, `preview/` and `installer/`, and
  `installer/` keeps only the current Setup plus its `logs/`. `installer.ps1`
  prunes superseded installers once the new one is verified, unless
  `-KeepOldInstallers` is passed for one not yet released.

## Refreshing the offline dictionary

The bundled `Assets/dictionary.json.gz` is exported from `../atlas.dict`:

```powershell
cd ..\atlas.dict
go run ./scripts/export-swl -out ..\SirWordALot\Assets\dictionary.json
python -c "import gzip; open('../SirWordALot/Assets/dictionary.json.gz','wb').write(gzip.compress(open('../SirWordALot/Assets/dictionary.json','rb').read(),9))"
Remove-Item ..\SirWordALot\Assets\dictionary.json
```

The source is a translation dictionary. Of its 74,656 senses, seven carry
English glosses, so it supplies Turkish meanings and nothing else; do not
promise definitions, phonetics or parts of speech from it.

These flows adapt clockt's `AGENTS.md` and the user's release-workflow and
no-commit-attribution notes to this repository.
