#!/usr/bin/env bash
set -euo pipefail

owner="FireTva"
repository="dave-the-diver-coop"
package_name="DaveDiverSteamInviteLinux.zip"
release_url="https://github.com/${owner}/${repository}/releases/latest/download"
expected_core_sha256="26d79570fc2a491ed18f9d4557ae6caa34882591219d55e3d236a5f04e7107f2"

usage() {
    printf 'Usage: %s [--game-dir PATH]\n' "$0"
    printf 'Installs the Steam-invite co-op mod into a Steam DAVE THE DIVER folder.\n'
}

die() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

game_dir=""
while (($#)); do
    case "$1" in
        --game-dir)
            (($# >= 2)) || die "--game-dir requires a path"
            game_dir="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            usage >&2
            die "Unknown argument: $1"
            ;;
    esac
done

for command_name in curl sha256sum tr unzip; do
    command -v "$command_name" >/dev/null 2>&1 || die "Required command not found: $command_name"
done

declare -a steam_libraries=()
declare -A seen_libraries=()
add_steam_library() {
    local path="${1/#\~/$HOME}"
    [[ -d "$path" ]] || return 0
    if [[ -z "${seen_libraries[$path]+present}" ]]; then
        steam_libraries+=("$path")
        seen_libraries["$path"]=1
    fi
}

if [[ -z "$game_dir" ]]; then
    for steam_root in \
        "$HOME/.steam/steam" \
        "$HOME/.local/share/Steam" \
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
        add_steam_library "$steam_root"
        library_file="$steam_root/steamapps/libraryfolders.vdf"
        if [[ -f "$library_file" ]]; then
            while IFS= read -r library; do
                library="${library//\\\\/\/}"
                add_steam_library "$library"
            done < <(sed -nE 's/.*"path"[[:space:]]*"([^"]+)".*/\1/p' "$library_file")
        fi
    done

    declare -a game_candidates=()
    for library in "${steam_libraries[@]}"; do
        candidate="$library/steamapps/common/DAVE THE DIVER"
        if [[ -f "$candidate/DaveTheDiver.exe" ]]; then
            game_candidates+=("$candidate")
        fi
    done

    if ((${#game_candidates[@]} == 0)); then
        die "Could not find the Steam game. Pass its folder with --game-dir."
    elif ((${#game_candidates[@]} == 1)); then
        game_dir="${game_candidates[0]}"
    else
        printf 'Found multiple game folders:\n'
        for index in "${!game_candidates[@]}"; do
            printf '  %d) %s\n' "$((index + 1))" "${game_candidates[$index]}"
        done
        read -r -p 'Select a folder number: ' selection
        [[ "$selection" =~ ^[0-9]+$ ]] || die "Invalid selection"
        selection_decimal=$((10#$selection))
        ((selection_decimal >= 1 && selection_decimal <= ${#game_candidates[@]})) || die "Selection out of range"
        game_dir="${game_candidates[$((selection_decimal - 1))]}"
    fi
fi

game_dir="$(cd "$game_dir" 2>/dev/null && pwd)" || die "Game folder does not exist."
[[ -f "$game_dir/DaveTheDiver.exe" ]] || die "DaveTheDiver.exe was not found in: $game_dir"

if [[ -f "$game_dir/BepInEx/core/BepInEx.Core.dll" ]]; then
    actual_core_sha256="$(sha256sum "$game_dir/BepInEx/core/BepInEx.Core.dll" | awk '{print $1}')"
    [[ "$actual_core_sha256" == "$expected_core_sha256" ]] \
        || die "A different BepInEx build is installed. Back it up and use the matching BepInEx 6.0.0-be.789 build."
elif [[ -e "$game_dir/BepInEx" || -e "$game_dir/winhttp.dll" || -e "$game_dir/doorstop_config.ini" ]]; then
    die "A partial BepInEx installation exists. Repair or back it up before installing."
fi

if [[ -d "$game_dir/dotnet" ]] \
    && find "$game_dir/dotnet" -type f -print -quit | grep -q .; then
    [[ -f "$game_dir/dotnet/.version" ]] \
        || die "A partial local .NET runtime exists without a version marker."
    grep -q '6\.0\.7' "$game_dir/dotnet/.version" \
        || die "A different local .NET runtime is installed. Back it up before installing."
fi

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/dave-diver-coop.XXXXXX")"
cleanup() {
    rm -rf -- "$work_dir"
}
trap cleanup EXIT

printf 'Downloading Steam-invite files for:\n  %s\n' "$game_dir"
curl --fail --location --retry 3 --silent --show-error \
    "$release_url/$package_name" \
    --output "$work_dir/$package_name"
curl --fail --location --retry 3 --silent --show-error \
    "$release_url/$package_name.sha256" \
    --output "$work_dir/$package_name.sha256"
tr -d '\r' < "$work_dir/$package_name.sha256" \
    | (cd "$work_dir" && sha256sum --check -)

mkdir "$work_dir/package"
unzip -q "$work_dir/$package_name" -d "$work_dir/package"
[[ -f "$work_dir/package/BepInEx/core/BepInEx.Core.dll" ]] \
    || die "Downloaded package does not contain BepInEx."
[[ -f "$work_dir/package/BepInEx/plugins/DaveTheDiverCoop/DaveTheDiverCoop.dll" ]] \
    || die "Downloaded package does not contain the Steam-invite plugin."

timestamp="$(date -u +%Y%m%d-%H%M%S)"
plugin_dir="$game_dir/BepInEx/plugins"
community_plugin="$plugin_dir/DaveTheDiverMP/DaveTheDiverMP.dll"
invite_plugin="$plugin_dir/DaveTheDiverCoop/DaveTheDiverCoop.dll"
invite_license="$plugin_dir/DaveTheDiverCoop/LICENSE.txt"

if [[ -f "$community_plugin" ]]; then
    mv -- "$community_plugin" "$community_plugin.disabled-$timestamp"
    printf 'Backed up and disabled Coop-Divers: %s.disabled-%s\n' "$community_plugin" "$timestamp"
fi

for existing in "$invite_plugin" "$invite_license"; do
    if [[ -f "$existing" ]]; then
        mv -- "$existing" "$existing.backup-$timestamp"
        printf 'Backed up existing file: %s.backup-%s\n' "$existing" "$timestamp"
    fi
done

cp -a -n -- "$work_dir/package/." "$game_dir/"

printf '\nSteam-invite co-op installed.\n'
printf 'In Steam, set the game launch option to:\n'
printf '  WINEDLLOVERRIDES="winhttp=n,b" %%command%%\n'
printf 'Keep the game closed during installation, and install the same mod on your friend''s PC.\n'
printf 'The mod is experimental; two-player gameplay has not been verified.\n'
