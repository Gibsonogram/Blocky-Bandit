#!/usr/bin/env bash
# Keep a Unity project folder usable from more than one local Linux account.
#
# Why this exists
# ---------------
# Unity writes new files atomically: it creates Temp/UnityTempFile-* and then
# renames it into place, and it rewrites Library/ScriptMapper, Library/*.asset
# and the whole Library/ import cache. If the account running the Editor cannot
# write inside Temp/ (or the cache), the launch dies with:
#
#   Failed to create temporary cache directory 'Temp/ADB-Refresh...'
#   Failed to write file 'Library/ScriptMapper' with error 3
#   Safe Mode: Only loading a subset of assemblies   (plus a wall of
#                                                    CS0433 Cecil errors)
#
# The usual trigger is the *other* account opening the project with umask 022.
# The project shares its cache through extended ACLs (user:alice, user:bob) and
# setgid directories, but POSIX ACL masks are computed as mask & ~umask when a
# directory is created. umask 022 therefore turns an inherited mask::rwx into
# mask::r-x, and the other account silently loses write access to Temp/.
#
# Usage
#   scripts/fix-unity-shared-perms.sh           # repair as your own account
#   sudo scripts/fix-unity-shared-perms.sh      # same, plus chown of strays
#   scripts/fix-unity-shared-perms.sh --check   # report only, change nothing
#
# Close the Unity Editor before running this.
set -euo pipefail

PROJECT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
CHECK=0
[[ "${1:-}" == "--check" ]] && CHECK=1

current_user="$(id -un)"
project_group="$(stat -c '%G' "$PROJECT")"

# ---------------------------------------------------------------- reporting --
report_bad_permissions() {
    local bad
    bad="$(find "$PROJECT" -xdev -path "$PROJECT/.git" -prune -o -not -writable -print 2>/dev/null | head -20 || true)"
    if [[ -n "$bad" ]]; then
        echo "not writable by $current_user:"
        while IFS= read -r p; do echo "  ${p#"$PROJECT"/}"; done <<<"$bad"
    else
        echo "every file in the project is writable by $current_user"
    fi
}

echo "project : $PROJECT"
echo "owner   : $(stat -c '%U' "$PROJECT"), group: $project_group, mode: $(stat -c '%a' "$PROJECT")"
echo "umask   : $(umask) (must be 002 for shared dirs, see below)"
for d in Temp Library Logs UserSettings; do
    [[ -e "$PROJECT/$d" ]] || continue
    printf '  %-13s %s\n' "$d" "$(stat -c '%U:%G %a' "$PROJECT/$d")"
done
report_bad_permissions

if ((CHECK)); then
    exit 0
fi

# -------------------------------------------------------------------- repair --
if pgrep -f 'Unity/Editor/Unity' >/dev/null 2>&1; then
    echo "error: the Unity Editor is running - close the project first." >&2
    exit 1
fi

if ((EUID == 0)) && [[ -n "${SUDO_USER:-}" ]]; then
    owner="$SUDO_USER"
else
    owner="$current_user"
fi

# Which accounts share this project? Explicit env var, else existing ACL users.
users=()
if [[ -n "${SHARED_USERS:-}" ]]; then
    IFS=', ' read -r -a users <<<"$SHARED_USERS"
fi
if ((${#users[@]} == 0)); then
    mapfile -t users < <(getfacl -p "$PROJECT" 2>/dev/null | awk -F: '/^user:[^:]+:/{print $2}' | sort -u)
fi
((${#users[@]})) || users=("$current_user")

if ((EUID == 0)); then
    chown -R "$owner:$project_group" "$PROJECT"
fi

# Temp/ is disposable: if it is not writable, move it aside and start clean.
if [[ -e "$PROJECT/Temp" && ! -w "$PROJECT/Temp" ]]; then
    aside="$PROJECT/Temp.stale-$(date +%Y%m%d-%H%M%S)"
    mv "$PROJECT/Temp" "$aside"
    echo "moved unwritable Temp aside -> ${aside#"$PROJECT"/}"
fi
[[ -d "$PROJECT/Temp" ]] || mkdir "$PROJECT/Temp"
rm -f "$PROJECT/Temp/UnityLockfile"

# The shared group (wheel here) is the primary mechanism, the per-user ACL
# entries are the fallback. Both need a rwx mask, on access and default ACLs.
acl_access=(-m "g::rwX" -m "m::rwX")
acl_default=(-m "d:g::rwX" -m "d:m::rwX")
for u in "${users[@]}"; do
    acl_access+=(-m "u:$u:rwX")
    acl_default+=(-m "d:u:$u:rwX")
done

chmod -R g+rwX "$PROJECT" 2>/dev/null || true
find "$PROJECT" -xdev -type d -exec chmod g+s {} + 2>/dev/null || true
find "$PROJECT" -xdev -path "$PROJECT/.git" -prune -o -exec setfacl "${acl_access[@]}" {} + 2>/dev/null || true
find "$PROJECT" -xdev -type d -exec setfacl "${acl_default[@]}" {} + 2>/dev/null || true

echo
echo "repaired. shared accounts: ${users[*]} (group: $project_group)"
report_bad_permissions

if ((8#$(umask) & 0020)); then
    echo
    echo "warning: this shell has umask $(umask). Directories it creates will not be" >&2
    echo "         group-writable and will lock the other account out again." >&2
    echo "         Add 'umask 002' to ~/.bashrc for every account that opens the project." >&2
fi
