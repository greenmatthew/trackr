# The project wiki.
#
# The pages in wiki/ are ordinary tracked files in THIS repository, so a change to a
# flag and a change to the page documenting it land in one commit. `trackr.wiki.git` is a
# publishing target, not a source of truth: `just docs::publish` copies the pages there.
#
# Consequence worth knowing: editing a page through the GitHub or Gitea wiki UI will be
# overwritten by the next publish. Edit the files here instead.

# Recipes in a module run from the module FILE's directory (just/), not the repository root.
set working-directory := '..'

WIKI_DIR := "wiki"

_default:
    @just --list docs

# Show which pages would change on the wiki, without touching it.
check:
    @just docs::_sync --dry-run

# Copy wiki/ to the wiki repositories on GitHub and Gitea.
publish message="docs: update wiki":
    @just docs::_sync "{{ message }}"

# Warn about pages that exist on neither side of a link.
lint:
    #!/usr/bin/env bash
    set -euo pipefail
    cd {{ WIKI_DIR }} 2>/dev/null || { echo "No {{ WIKI_DIR }} yet."; exit 0; }
    fail=0
    # Wiki links are page names, not paths: [text](Self-Hosting) resolves to Self-Hosting.md.
    for f in *.md; do
        [ -e "$f" ] || continue
        grep -oE '\]\([A-Z][A-Za-z0-9-]*\)' "$f" 2>/dev/null | tr -d '](' | tr -d ')' | while read -r page; do
            if [ ! -f "${page}.md" ]; then
                echo "$f -> ${page}.md is missing"
            fi
        done
    done
    echo "Pages: $(ls -1 *.md 2>/dev/null | wc -l)"

# The actual sync. Not called directly - use `publish` or `check`.
[private]
_sync *args:
    #!/usr/bin/env bash
    set -euo pipefail

    dry=0
    message="docs: update wiki"
    if [ "${1:-}" = "--dry-run" ]; then dry=1; else message="${1:-$message}"; fi

    if [ ! -d "{{ WIKI_DIR }}" ] || [ -z "$(ls -A {{ WIKI_DIR }} 2>/dev/null)" ]; then
        echo "error: {{ WIKI_DIR }} is empty - nothing to publish." >&2
        exit 1
    fi

    # Derived from this repository's own remotes rather than hardcoded, so the URLs cannot
    # drift apart: <repo>.git -> <repo>.wiki.git, which is how both GitHub and Gitea name
    # the wiki repository.
    wiki_url() { git remote get-url "$1" 2>/dev/null | sed 's/\.git$/.wiki.git/'; }

    primary=$(wiki_url local || true)
    [ -n "$primary" ] || primary=$(wiki_url github)

    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT

    if ! git clone --quiet "$primary" "$tmp" 2>/dev/null; then
        echo "error: could not clone $primary" >&2
        echo "" >&2
        echo "A wiki repository does not exist until the wiki has at least one page." >&2
        echo "Create it once per host, then re-run:" >&2
        echo "  GitHub  repo -> Wiki -> Create the first page -> Save" >&2
        echo "  Gitea   repo -> Settings -> enable Wiki, then create the first page" >&2
        exit 1
    fi

    # --delete so a page removed here is removed from the wiki too. Excluding .git is what
    # keeps that from destroying the wiki repository itself.
    rsync -a --delete --exclude '.git' {{ WIKI_DIR }}/ "$tmp/"

    cd "$tmp"
    if git diff --quiet && git diff --cached --quiet && [ -z "$(git status --porcelain)" ]; then
        echo "Wiki is already up to date."
        exit 0
    fi

    git add -A
    if [ "$dry" = "1" ]; then
        echo "Would publish:"
        git --no-pager diff --cached --stat
        exit 0
    fi

    git -c user.name="$(git -C {{ justfile_directory() }} config user.name)" \
        -c user.email="$(git -C {{ justfile_directory() }} config user.email)" \
        commit --quiet -m "$message"

    for remote in local github; do
        url=$(cd {{ justfile_directory() }} && git remote get-url "$remote" 2>/dev/null | sed 's/\.git$/.wiki.git/') || continue
        [ -n "$url" ] || continue
        echo "pushing to $remote ($url)"
        git push --quiet "$url" HEAD:master || echo "  warning: push to $remote failed"
    done

    echo "Published $(ls -1 {{ justfile_directory() }}/{{ WIKI_DIR }}/*.md 2>/dev/null | wc -l) pages."
