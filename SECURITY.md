# Security and privacy

This is a public repository. Do not commit credentials, personal email addresses, local user-directory paths, private production notes, or unpublished user data.

## Repository controls

- GitHub secret scanning and push protection remain enabled.
- Repository-local Git identity uses the GitHub-provided no-reply address.
- Versioned pre-commit and pre-push hooks run `scripts/check-public-privacy.mjs`.
- GitHub Actions runs the same scan on pushes and pull requests.
- The scanner reports only the file, line, and rule. It does not print matched values.

Install the local controls after cloning:

```powershell
.\scripts\install-git-hooks.ps1
```

Run the scanner manually:

```powershell
node .\scripts\check-public-privacy.mjs --all
```

If sensitive information is found after publication, remove it from the current tree, rotate any affected credential, rewrite every reachable Git ref, and request cache cleanup from GitHub Support when an unreachable commit remains accessible.
