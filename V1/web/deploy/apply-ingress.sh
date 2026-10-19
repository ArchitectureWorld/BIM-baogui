#!/usr/bin/env bash
set -Eeuo pipefail
[[ $EUID == 0 && $(hostname) == pc ]] || exit 3
base=/srv/hifc-web
chain=HIFC-WEB-INGRESS
iptables -w 10 -nL DOCKER-USER >/dev/null
if iptables -w 10 -nL "$chain" >/dev/null 2>&1 && [[ ! -f "$base/config/ingress-managed" ]]; then
  echo 'EXISTING_UNOWNED_FIREWALL_CHAIN'; exit 3
fi
clients=$(python3 - "$base/config/allowed-clients.txt" <<'PY'
import ipaddress, sys
addresses=[]
for text in open(sys.argv[1]):
    value=text.strip()
    if not value: continue
    network=ipaddress.IPv4Network(value, strict=True)
    if not network.is_private or network.prefixlen < 16: raise RuntimeError('Only approved private office networks are allowed')
    addresses.append(str(network))
if not addresses: raise RuntimeError('Empty source allowlist')
print('\n'.join(addresses))
PY
)
rules=$(mktemp "$base/logs/ingress.XXXXXX")
trap 'rm -f -- "$rules"' EXIT
{
  printf '*filter\n:%s - [0:0]\n-F %s\n' "$chain" "$chain"
  while IFS= read -r client; do
    printf -- '-A %s -p tcp -s %s -m conntrack --ctdir ORIGINAL --ctorigdst 192.168.6.45 --ctorigdstport 8443 -j RETURN\n' "$chain" "$client"
  done <<< "$clients"
  printf -- '-A %s -p tcp -m conntrack --ctdir ORIGINAL --ctorigdst 192.168.6.45 --ctorigdstport 8443 -j DROP\n' "$chain"
  printf -- '-A %s -j RETURN\nCOMMIT\n' "$chain"
} > "$rules"
iptables-restore --wait 10 --noflush < "$rules"
iptables -w 10 -C DOCKER-USER -j "$chain" 2>/dev/null || iptables -w 10 -I DOCKER-USER 1 -j "$chain"
touch "$base/config/ingress-managed"
printf 'HIFC_INGRESS_READY\n'
