# Releasing `Spintax.Core`

Tag-driven, and deliberately not automatic on push: a release is a decision, not a side effect of
merging. Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`, which **re-verifies** the
tagged commit (build with warnings as errors, `dotnet format`, the unit tests on both hosts, the
golden corpus on both hosts, checked out from `investblog/spintax-js`), **packs**, **pushes** to
NuGet and **announces** a GitHub release. The workflow re-runs the gate on purpose: a tag can be
placed on any commit, including one CI never saw green, and the artifact that reaches NuGet must be
one that passed — not one that probably did.

There is no CHANGELOG file in this repo. **The tag annotation IS the release note**: its subject
becomes the GitHub release title, its body the release notes. Write it at the moment the release is
decided; the workflow publishes it verbatim.

## Preconditions

1. `main` is green in CI.
2. If the release covers a cross-engine fix, its corpus fixtures are already on `spintax-js@main` —
   the verify job pulls fixtures from there, so a fixture landing *after* the tag never gated the
   tagged artifact.
3. `src/Spintax.Core/Spintax.Core.csproj` `<Version>` is bumped: the workflow **fails the release if
   the csproj version does not match the tag** — that gate is why the bump commit must land before
   (or be) the tagged commit.
4. **Trusted Publishing** is set up — no long-lived key anywhere:
   - on nuget.org: *username → Trusted Publishing → Add policy* with Repository Owner
     `investblog`, Repository `spintax-dotnet`, Workflow File `release.yml`, Environment `nuget`
     (the policy applies to every package of its owner; for a public repository it is active
     at once, and the first successful publish pins it to the repository's ID);
   - on GitHub: the secret `NUGET_USER` in the `nuget` environment = the nuget.org **profile
     name** (not an e-mail). The workflow's `NuGet/login@v1` step exchanges the job's OIDC token
     for a one-hour API key right before `dotnet nuget push`.

## Versioning

While 0.x: a behaviour fix toward the family contract is a **patch**; anything that widens or
changes the public API is a **minor**. The first published version is `0.1.0`, the same number the
engine carried inside `spintax-zenno`.

## Cutting a release

```sh
# 1. Bump the version
#    src/Spintax.Core/Spintax.Core.csproj: <Version>X.Y.Z</Version>

# 2. Release commit (convention: "Release X.Y.Z")
git add src/Spintax.Core/Spintax.Core.csproj && git commit -m "Release X.Y.Z"
git push origin main

# 3. Wait for CI on that exact commit, then tag it — annotated; the annotation is the
#    release note (subject line + blank line + body).
git tag -a vX.Y.Z -m "Spintax.Core X.Y.Z — one-line headline

A few sentences: what changed, what it was measured against, corpus numbers."

# 4. Push the tag — this triggers Release (verify → pack → push → announce).
git push origin vX.Y.Z
```

## What the workflow enforces (so you don't have to)

- Full gate re-run on the tagged commit: build, format, tests, corpus — on `windows-latest`, so
  the `net472` host really runs.
- **csproj Version == tag** — a mismatched bump stops before upload.
- `dotnet pack` with the symbol package (`.snupkg`) and the README inside the package.
- Keyless push through Trusted Publishing (`id-token: write`, `NuGet/login@v1`); `--skip-duplicate` on push: re-running a tag that already published is a no-op, not an error.
- The GitHub release is created only after NuGet accepted the upload, so a release never announces
  an artifact that wasn't published.

## After the tag

```sh
gh run watch --repo investblog/spintax-dotnet
curl -s https://api.nuget.org/v3-flatcontainer/spintax.core/index.json
gh release view vX.Y.Z --repo investblog/spintax-dotnet
```

## Un-shipping a mistake

NuGet never lets a version be re-uploaded — deleting (unlisting) hides the page, but the number is
burned. Ship `X.Y.(Z+1)`; unlist only to stop new installs of a broken version (existing pins keep
resolving). After the first package, reserve the `Spintax.*` ID prefix on nuget.org so that
`Spintax.Net` and later packages stay with the family.
