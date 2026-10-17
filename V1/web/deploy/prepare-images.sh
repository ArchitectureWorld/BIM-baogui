#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
[[ $EUID == 0 && $(hostname) == pc ]] || exit 3
stage=/home/zetta/hifc-web-bootstrap-20261017
dropin=/run/systemd/system/docker.service.d/hifc-download-proxy.conf
[[ -z $(docker ps -q) ]] || { echo 'RUNNING_CONTAINERS_NO_DOCKER_RESTART'; exit 3; }
[[ ! -e "$dropin" ]] || { echo 'EXISTING_TEMPORARY_PROXY_REQUIRES_REVIEW'; exit 3; }
curl --proxy http://127.0.0.1:18089 --silent --show-error --head --connect-timeout 10 --max-time 20 https://registry-1.docker.io/v2/ >/dev/null
install -d /run/systemd/system/docker.service.d
cat > "$dropin" <<'PROXY'
[Service]
Environment="HTTP_PROXY=http://127.0.0.1:18089"
Environment="HTTPS_PROXY=http://127.0.0.1:18089"
Environment="NO_PROXY=localhost,127.0.0.1,::1"
PROXY
cleanup() {
  local result=$?
  if [[ -n $(docker ps -q) ]]; then echo 'PROXY_CLEANUP_DEFERRED_RUNNING_CONTAINERS'; return 3; fi
  rm -f -- "$dropin"
  systemctl daemon-reload
  systemctl restart docker
  return "$result"
}
trap cleanup EXIT
systemctl daemon-reload
systemctl restart docker
for image in hello-world:latest postgres:17 caddy:2 mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled; do
  log="$stage/pull-${image//\//_}.log"
  ready=false
  for attempt in 1 2 3; do
    printf 'PULL %s attempt=%s\n' "$image" "$attempt"
    if docker pull "$image" >> "$log" 2>&1; then ready=true; break; fi
    sleep 3
  done
  "$ready" || { tail -n 12 "$log"; exit 1; }
done
docker run --rm --network none hello-world > "$stage/hello-world.log"
python3 - "$stage/image-lock.json" <<'PY'
import json, subprocess, sys
images={}
for name, tag in [('postgres','postgres:17'), ('caddy','caddy:2'), ('runtime','mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled')]:
    info=json.loads(subprocess.check_output(['docker','image','inspect',tag]))[0]
    if info['Architecture'] != 'amd64' or info['Os'] != 'linux': raise RuntimeError('Image architecture mismatch')
    images[name]={'reference':info['RepoDigests'][0], 'imageId':info['Id'], 'sourceTag':tag}
with open(sys.argv[1], 'w') as output: json.dump(images, output, indent=2)
PY
chmod 0644 "$stage/image-lock.json"
printf 'HIFC_IMAGES_CACHED\n'
