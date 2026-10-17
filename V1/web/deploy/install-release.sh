#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
[[ $EUID == 0 && $(hostname) == pc ]] || exit 3
release_id=${1:?release id}
archive=${2:?archive}
expected_hash=${3:?archive sha256}
allowed_client=${4:?approved IPv4 client}
[[ "$release_id" =~ ^0\.5\.0-dev-[a-zA-Z0-9._-]+$ && "$expected_hash" =~ ^[a-fA-F0-9]{64}$ ]] || exit 3
python3 - "$allowed_client" <<'PY'
import ipaddress, sys
ipaddress.IPv4Address(sys.argv[1])
PY
base=/srv/hifc-web
release="$base/releases/$release_id"
stage=/home/zetta/hifc-web-bootstrap-20261017
[[ $(sha256sum "$archive" | cut -d' ' -f1) == "${expected_hash,,}" ]] || { echo 'ARCHIVE_HASH_MISMATCH'; exit 3; }
for path in "$base" "$base/releases" "$base/config" "$base/secrets" "$base/data" "$base/data/server" "$base/data/caddy" "$base/data/postgres" "$release"; do
  [[ ! -L "$path" ]] || { echo 'UNEXPECTED_SYMLINK'; exit 3; }
done
if [[ -e "$base/current" ]]; then
  [[ -L "$base/current" && $(readlink -f "$base/current") == "$base/releases/"* ]] || exit 3
fi
if [[ -e "$release" ]]; then echo 'RELEASE_ALREADY_EXISTS'; exit 3; fi
install -d -m 0750 "$base" "$base/releases" "$base/config" "$base/data" "$base/logs" "$release"
install -d -m 0700 "$base/secrets"
python3 - "$archive" <<'PY'
import pathlib, sys, tarfile
with tarfile.open(sys.argv[1]) as archive:
    for item in archive:
        path=pathlib.PurePosixPath(item.name)
        if path.is_absolute() or '..' in path.parts or not (item.isdir() or item.isfile()): raise RuntimeError('Unsafe archive entry')
PY
tar --no-same-owner --no-same-permissions -xf "$archive" -C "$release"
cp "$stage/image-lock.json" "$release/image-lock.json"
for name in app_db_password postgres_admin_password; do
  [[ ! -L "$base/secrets/$name" ]] || exit 3
  if [[ ! -e "$base/secrets/$name" ]]; then openssl rand -base64 36 > "$base/secrets/$name"; fi
  chmod 0444 "$base/secrets/$name"
done
install -d -m 0700 -o 1654 -g 1654 "$base/data/server"
install -d -m 0700 -o 1000 -g 1000 "$base/data/caddy"
if [[ ! -d "$base/data/postgres" ]]; then install -d -m 0700 "$base/data/postgres"; fi
printf '%s/32\n' "$allowed_client" > "$base/config/allowed-clients.txt"
python3 - "$release" "$allowed_client" <<'PY'
import json, pathlib, sys
release=pathlib.Path(sys.argv[1])
template=(release/'Caddyfile.template').read_text()
(release/'Caddyfile').write_text(template.replace('__ALLOWED_CLIENT__',sys.argv[2]+'/32'))
PY
chmod 0444 "$release/Caddyfile" "$release/postgres-init.sql"
runtime=$(python3 -c 'import json,sys; item=json.load(open(sys.argv[1]))["runtime"]; print(item.get("buildReference",item["reference"]))' "$release/image-lock.json")
tag="hifc-web:$release_id"
docker build --pull=false --network none --build-arg "BASE_IMAGE=$runtime" -t "$tag" "$release"
app_image=$(docker image inspect --format '{{.Id}}' "$tag")
python3 - "$release" "$app_image" <<'PY'
import json, pathlib, sys
release=pathlib.Path(sys.argv[1]); images=json.loads((release/'image-lock.json').read_text()); metadata=json.loads((release/'release.json').read_text())
values={'POSTGRES_IMAGE':images['postgres']['reference'], 'CADDY_IMAGE':images['caddy']['reference'], 'HIFC_IMAGE':sys.argv[2], 'BUILD_REVISION':metadata['sourceCommit']}
(release/'.env').write_text(''.join(name+'='+value+'\n' for name,value in values.items()))
metadata.update({'appImage':sys.argv[2], 'authenticationConfigured':False, 'experienceAcceptancePassed':False})
(release/'release.json').write_text(json.dumps(metadata,indent=2))
PY
docker compose --project-directory "$release" --project-name hifc-web config --quiet
install -d -m 0755 /usr/local/lib/hifc-web
install -m 0755 "$release/apply-ingress.sh" /usr/local/lib/hifc-web/apply-ingress.sh
for unit in hifc-web-ingress.service hifc-web.service; do
  if [[ -e "/etc/systemd/system/$unit" ]] && ! grep -q 'HIFC Web' "/etc/systemd/system/$unit"; then echo 'UNOWNED_SYSTEMD_UNIT'; exit 3; fi
  install -m 0644 "$release/$unit" "/etc/systemd/system/$unit"
done
ln -s "$release" "$base/current.new"
mv -T "$base/current.new" "$base/current"
systemctl daemon-reload
systemctl enable hifc-web-ingress.service hifc-web.service
systemctl restart hifc-web-ingress.service
systemctl restart hifc-web.service
docker compose --project-directory "$release" --project-name hifc-web ps
printf 'HIFC_WEB_SERVER_FOUNDATION_DEPLOYED\n'
