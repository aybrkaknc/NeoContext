# winget manifest

These files are a template for submitting NeoContext to the
[Windows Package Manager community repository](https://github.com/microsoft/winget-pkgs).

The manifest installs the portable zip, so it works without a code-signing certificate.

## Submitting a release

1. Publish a GitHub release (push a `v*` tag; the release workflow builds the artifacts).
2. Copy the three `.yaml` files and replace the placeholders:
   - `{{VERSION}}`: the release version without the `v`, for example `0.2.0`.
   - `{{SHA256_X64}}` / `{{SHA256_ARM64}}`: from `SHA256SUMS.txt` in the release, upper case.
   - `{{RELEASE_DATE}}`: the release date as `YYYY-MM-DD`.
3. Validate locally: `winget validate --manifest <folder>` and `winget install --manifest <folder>`.
4. Open a pull request to `microsoft/winget-pkgs` under `manifests/a/aybrkaknc/NeoContext/{{VERSION}}/`
   (or use [`wingetcreate`](https://github.com/microsoft/winget-create), which fills in hashes for you).
