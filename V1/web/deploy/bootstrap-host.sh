#!/usr/bin/env bash
set -Eeuo pipefail

# Official Docker Engine / Compose installation for this Web service only.
# Default is read-only. --apply requires interactive sudo authentication.
mode="${1:---check}"
docker_apt_uri="${2:-https://download.docker.com/linux/ubuntu}"
if [[ "$mode" != --check && "$mode" != --apply ]]; then
  echo 'usage: bootstrap-host.sh [--check|--apply]' >&2; exit 64
fi
case "$docker_apt_uri" in
  https://download.docker.com/linux/ubuntu|https://mirrors.tuna.tsinghua.edu.cn/docker-ce/linux/ubuntu) ;;
  *) echo 'UNRECOGNIZED_DOCKER_APT_MIRROR' >&2; exit 64 ;;
esac
docker_source() {
  printf 'Types: deb\nURIs: %s\nSuites: noble\nComponents: stable\nArchitectures: amd64\nSigned-By: /etc/apt/keyrings/docker.asc\n' "$1"
}
if [[ "$(id -un)" != zetta && "$EUID" != 0 ]] || [[ "$(hostname)" != pc ]]; then
  echo 'HOST_IDENTITY_MISMATCH' >&2; exit 3
fi
source /etc/os-release
if [[ "$ID" != ubuntu || "$VERSION_ID" != 24.04 || "$(dpkg --print-architecture)" != amd64 ]]; then
  echo 'EXPECTED_UBUNTU_24_04_AMD64' >&2; exit 3
fi
failures=0
if command -v docker >/dev/null 2>&1; then
  docker --version
  if ! docker compose version; then echo 'COMPOSE_MISSING'; failures=$((failures+1)); fi
else
  echo 'DOCKER_MISSING'; failures=$((failures+1))
fi
for port in 8443 9443; do
  if ss -H -ltn | awk '{print $4}' | grep -Eq ":${port}$"; then
    echo "PORT_IN_USE:${port}"; failures=$((failures+1))
  fi
done
if [[ -L /srv/hifc-web ]]; then echo 'TARGET_IS_SYMLINK'; exit 3; fi
if [[ -e /srv/hifc-web && ! -d /srv/hifc-web ]]; then echo 'TARGET_IS_NOT_DIRECTORY'; exit 3; fi
if [[ "$mode" == --check ]]; then
  if sudo -n true >/dev/null 2>&1; then echo 'SUDO_NONINTERACTIVE_AVAILABLE'; else echo 'SUDO_INTERACTIVE_REQUIRED'; fi
  echo "failures=${failures}"
  exit "$(( failures > 0 ? 2 : 0 ))"
fi
if [[ "$EUID" != 0 ]]; then echo 'RUN_WITH_INTERACTIVE_SUDO' >&2; exit 4; fi
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=l
if ss -H -ltn | awk '{print $4}' | grep -Eq ':(8443|9443)$'; then echo 'PORT_CONFLICT_NO_CHANGES_MADE'; exit 3; fi

backup="/var/backups/hifc-web-bootstrap/$(date -u +%Y%m%dT%H%M%SZ)"
install -d -m 0700 "$backup"
ss -H -ltn > "$backup/listeners-before.txt"
if command -v iptables-save >/dev/null 2>&1; then iptables-save > "$backup/iptables-before.txt"; fi
if command -v nft >/dev/null 2>&1; then nft list ruleset > "$backup/nft-before.txt"; fi
forward_policy=$(iptables -S FORWARD 2>/dev/null | head -n 1 | awk '{print $3}')
if ! command -v docker >/dev/null 2>&1; then
  for package in docker.io docker-compose docker-compose-v2 podman-docker containerd runc; do
    if dpkg-query -W -f='${db:Status-Status}' "$package" 2>/dev/null | grep -qx installed; then
      echo "EXISTING_PACKAGE_REQUIRES_REVIEW:${package}" >&2; exit 5
    fi
  done
  if [[ -e /etc/apt/sources.list.d/docker.sources ]]; then
    if ! cmp -s /etc/apt/sources.list.d/docker.sources <(docker_source https://download.docker.com/linux/ubuntu) &&
       ! cmp -s /etc/apt/sources.list.d/docker.sources <(docker_source https://mirrors.tuna.tsinghua.edu.cn/docker-ce/linux/ubuntu); then
      echo 'EXISTING_DOCKER_APT_SOURCE_REQUIRES_REVIEW' >&2; exit 5
    fi
    cp -a /etc/apt/sources.list.d/docker.sources "$backup/docker.sources.before"
  fi
  apt-get update
  apt-get install -y ca-certificates curl
  install -m 0755 -d /etc/apt/keyrings
  if [[ ! -s /etc/apt/keyrings/docker.asc ]]; then
    curl --ipv4 --fail --show-error --location --retry 3 --retry-all-errors --connect-timeout 10 --max-time 60 https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  fi
  chmod a+r /etc/apt/keyrings/docker.asc
  docker_source "$docker_apt_uri" > /etc/apt/sources.list.d/docker.sources
  apt-get -o APT::Update::Error-Mode=any update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker.service
fi
if [[ "$forward_policy" == ACCEPT ]]; then
  [[ -z $(docker ps -q) ]] || { echo 'RUNNING_CONTAINERS_NO_DOCKER_RESTART'; exit 5; }
  [[ ! -L /etc/docker/daemon.json ]] || exit 5
  python3 - "$backup" <<'PY'
import json, pathlib, shutil, sys
path=pathlib.Path('/etc/docker/daemon.json')
config=json.loads(path.read_text()) if path.exists() else {}
if path.exists(): shutil.copy2(path, pathlib.Path(sys.argv[1])/'daemon.json.before')
config['ip-forward-no-drop']=True
path.write_text(json.dumps(config,indent=2)+'\n')
path.chmod(0o644)
PY
  dockerd --validate --config-file /etc/docker/daemon.json
  systemctl restart docker
  iptables -P FORWARD ACCEPT
fi
docker version
docker compose version
docker run --rm hello-world
if [[ -e /srv/hifc-web && "$(stat -c %u /srv/hifc-web)" != 0 ]]; then echo 'TARGET_OWNER_REQUIRES_REVIEW'; exit 5; fi
install -d -m 0750 /srv/hifc-web /srv/hifc-web/releases /srv/hifc-web/config /srv/hifc-web/data /srv/hifc-web/logs
install -d -m 0700 /srv/hifc-web/secrets
ss -H -ltn > "$backup/listeners-after.txt"
dpkg-query -W docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin > "$backup/versions.txt"
echo "HIFC_WEB_HOST_READY backup=${backup}"
echo 'No user was added to the Docker group. No Web service or new LAN listener has been started.'
