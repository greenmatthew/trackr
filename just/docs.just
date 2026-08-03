# The project wiki.
#
# The pages in wiki/ are ordinary tracked files in THIS repository, so a change to a flag and
# a change to the page documenting it land in one commit. The `<repo>.wiki.git` repositories
# are publishing targets, not sources of truth: `just docs::publish` copies the pages there.
#
# Consequence worth knowing: editing a page through the GitHub or Gitea wiki UI will be
# overwritten by the next publish. Edit the files here instead.
#
# `just` shows the LAST comment line above a recipe as its `--list` description, and echoes
# any comment left inside a recipe body when it runs. So longer explanations sit above the
# recipe, separated from the one-line description by a blank line.

# Recipes in a module run from the module FILE's directory (just/), not the repository root.
set working-directory := '..'

WIKI_DIR := "wiki"

# Where to publish. Derived from the `all` remote's push URLs - the same list `git push all`
# already uses - with `.git` swapped for `.wiki.git`. There is deliberately no separate list
# of wiki remotes to keep in step: adding a host to `all` is enough.
#
# Note a wiki repository does not exist until its wiki has been given one page through the
# web UI, and GitHub only offers a wiki on a public repository.
REMOTE := "all"

_default:
    @just --list docs

# Needs neither Docker nor the Android SDK - it only reads tracked files.

# Check the wiki against the code: env vars, just recipes, and the API reference.
test:
    dotnet test tests/Trackr.Docs.Tests

# Needs Docker: the generator boots the real application through WebApplicationFactory, and
# startup applies migrations against a throwaway Postgres. Without the variable the same test
# asserts instead of writing, which is what makes a stale page fail `just test`.

# Regenerate wiki/API-Reference.md from the API's own OpenAPI document.
api:
    TRACKR_UPDATE_DOCS=1 dotnet test tests/Trackr.Api.Tests --filter FullyQualifiedName~ApiReference

# Show which pages would change on each wiki, without touching them.
check:
    @TRACKR_WIKI_DRY=1 just docs::_sync

# Copy wiki/ to every wiki repository.
publish message="docs: update wiki":
    @TRACKR_WIKI_MESSAGE="{{ message }}" just docs::_sync

# Warn about links pointing at pages that do not exist.
lint:
    #!/usr/bin/env bash
    # No pipefail here: grep exits 1 when a page contains no links, which is not an error.
    set -u
    cd {{ WIKI_DIR }} 2>/dev/null || { echo "No {{ WIKI_DIR }} yet."; exit 0; }

    missing=0
    # Wiki links are page names, not paths: [text](Self-Hosting) resolves to Self-Hosting.md.
    for f in *.md; do
        [ -e "$f" ] || continue
        for page in $(grep -oE '\]\([A-Z][A-Za-z0-9-]*\)' "$f" | tr -d '])' | sed 's/^(//'); do
            if [ ! -f "${page}.md" ]; then
                echo "  $f -> ${page}.md is missing"
                missing=$((missing + 1))
            fi
        done
    done

    pages=$(ls -1 *.md 2>/dev/null | wc -l)
    if [ "$missing" -gt 0 ]; then
        echo "$pages pages, $missing dangling link(s)."
        exit 1
    fi
    echo "$pages pages, no dangling links."

# The actual sync. Not called directly - use `publish` or `check`.
#
# Each wiki is cloned, updated and pushed independently rather than pushing one synthesised
# history to every host. The wikis have unrelated histories - each starts from whatever page
# was created through its own web UI - so a single shared commit is rejected as a
# non-fast-forward everywhere except the host it was built from.
#
# Options arrive as environment variables rather than recipe arguments: a shebang recipe is
# written to a temporary script and executed, so `$1` inside it is NOT what the caller passed
# to `just`. Reading `$1` silently found nothing, which once made `check` publish for real.
[private]
_sync:
    #!/usr/bin/env bash
    set -euo pipefail

    dry="${TRACKR_WIKI_DRY:-0}"
    message="${TRACKR_WIKI_MESSAGE:-docs: update wiki}"

    if [ ! -d "{{ WIKI_DIR }}" ] || [ -z "$(ls -A {{ WIKI_DIR }} 2>/dev/null)" ]; then
        echo "error: {{ WIKI_DIR }} is empty - nothing to publish." >&2
        exit 1
    fi

    urls=$(git remote get-url --push --all {{ REMOTE }} | sed 's/\.git$/.wiki.git/')
    if [ -z "$urls" ]; then
        echo "error: remote '{{ REMOTE }}' has no push URLs." >&2
        exit 1
    fi

    name=$(git config user.name)
    email=$(git config user.email)
    root=$(pwd)
    failed=0

    for url in $urls; do
        echo "--- $url"
        tmp=$(mktemp -d)

        if ! git clone --quiet "$url" "$tmp" 2>/dev/null; then
            echo "  skipped: could not clone."
            echo "  A wiki repository does not exist until its wiki has one page. Create it"
            echo "  through the web UI once, then re-run. GitHub needs a public repository."
            rm -rf "$tmp"
            failed=1
            continue
        fi

        # --delete so a page removed here is removed from the wiki too. Excluding .git is
        # what stops that from destroying the wiki repository itself.
        rsync -a --delete --exclude '.git' "$root/{{ WIKI_DIR }}/" "$tmp/"

        if [ -z "$(git -C "$tmp" status --porcelain)" ]; then
            echo "  already up to date."
            rm -rf "$tmp"
            continue
        fi

        git -C "$tmp" add -A

        if [ "$dry" = "1" ]; then
            echo "  would publish:"
            git -C "$tmp" --no-pager diff --cached --stat | sed 's/^/  /'
            rm -rf "$tmp"
            continue
        fi

        git -C "$tmp" -c user.name="$name" -c user.email="$email" \
            commit --quiet -m "$message"

        if git -C "$tmp" push --quiet origin HEAD:master 2>/dev/null; then
            echo "  pushed $(ls -1 {{ WIKI_DIR }}/*.md | wc -l) pages."
        else
            echo "  push failed."
            failed=1
        fi

        rm -rf "$tmp"
    done

    exit $failed
