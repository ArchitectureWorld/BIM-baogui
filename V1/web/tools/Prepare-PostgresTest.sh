#!/usr/bin/env bash
# Temporary integration-test database. No sudo or system service changes.
set -euo pipefail
umask 077
[[ $(id -un) == zetta && $(hostname) == pc ]] || { echo 'Unexpected test host.' >&2; exit 1; }
base=/home/zetta/hifc-web-dev-20261017/postgres-test
root="$base/packages"
bin="$root/usr/lib/postgresql/16/bin"
socket="$base/socket"
mkdir -p "$base/downloads" "$root" "$socket"
chmod 700 "$base" "$socket"
if [[ ! -x "$bin/postgres" ]]; then
  cd "$base/downloads"
  apt-get download postgresql-16 postgresql-client-16 libpq5
  for package in ./*.deb; do dpkg-deb --extract "$package" "$root"; done
fi
export LD_LIBRARY_PATH="$root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
if ldd "$bin/postgres" | grep -q 'not found'; then
  ldd "$bin/postgres" >&2
  echo 'Missing PostgreSQL dependencies; test setup stopped.' >&2
  exit 1
fi
if [[ ! -f "$base/data/PG_VERSION" ]]; then
  "$bin/initdb" -D "$base/data" -L "$root/usr/share/postgresql/16" --encoding=UTF8 --locale=C.UTF-8 --auth-local=peer --auth-host=reject > "$base/init.log"
fi
case "${1:---start}" in
  --start)
    if ! "$bin/pg_ctl" -D "$base/data" status >/dev/null 2>&1; then
      "$bin/pg_ctl" -D "$base/data" -l "$base/server.log" -o "-c listen_addresses='' -k $socket -p 55432" -w start
    fi
    if [[ $("$bin/psql" -h "$socket" -p 55432 -d postgres -Atc "SELECT count(*) FROM pg_database WHERE datname='hifc_web_test'") == 0 ]]; then
      "$bin/createdb" -h "$socket" -p 55432 hifc_web_test
    fi
    ;;
  --restart)
    "$bin/pg_ctl" -D "$base/data" -l "$base/server.log" -m fast -w restart
    ;;
  --stop)
    "$bin/pg_ctl" -D "$base/data" -m fast -w stop
    exit 0
    ;;
  *) echo 'Usage: --start | --restart | --stop' >&2; exit 2 ;;
esac
"$bin/psql" -h "$socket" -p 55432 -d hifc_web_test -Atc 'SELECT version()'
printf 'HIFC_TEST_DB_READY\n'
