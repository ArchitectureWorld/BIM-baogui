#!/usr/bin/env bash
set -Eeuo pipefail
[[ $EUID == 0 && $(hostname) == pc ]] || exit 3
mode=${1:---before-recreate}
[[ "$mode" == --before-recreate || "$mode" == --after-recreate ]] || exit 3
release=$(readlink -f /srv/hifc-web/current)
[[ "$release" == /srv/hifc-web/releases/* ]] || exit 3
test_root=${HIFC_VALIDATION_TESTS_ROOT:-$release}
[[ "$test_root" == "$release" || "$test_root" == /srv/hifc-web/validation/* ]] || exit 3
[[ -d "$test_root/database-tests" && ! -L "$test_root" ]] || exit 3
stage=/home/zetta/hifc-web-bootstrap-20261017
runtime=$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["runtime"]["reference"])' "$release/image-lock.json")
install -d -m 0700 -o 1654 -g 1654 /srv/hifc-web/validation
compose=(docker compose --project-directory "$release" --project-name hifc-web)
database_run() {
  docker run --rm --network hifc_web_backend --entrypoint dotnet --read-only --tmpfs /tmp:size=32m --user 1654:1654 \
    --mount "type=bind,src=$test_root/database-tests,dst=/tests,readonly" \
    --mount type=bind,src=/srv/hifc-web/secrets/app_db_password,dst=/run/secrets/app_db_password,readonly \
    --mount type=bind,src=/srv/hifc-web/validation,dst=/validation \
    --env 'HIFC_TEST_DATABASE=Host=postgres;Port=5432;Username=hifc_web;Database=hifc_web_test;SSL Mode=Disable;Timeout=15;Command Timeout=15' \
    --env HIFC_TEST_DATABASE_PASSWORD_FILE=/run/secrets/app_db_password \
    --workdir /tests "$runtime" Hifc.Web.DatabaseTests.dll "$@"
}
if [[ "$mode" == --before-recreate ]]; then
  for spec in 'web-tests Hifc.Web.Tests.dll' 'rule-tests Hifc.Web.RuleRegression.dll'; do
    read -r directory assembly <<< "$spec"
    docker run --rm --network none --entrypoint dotnet --read-only --tmpfs /tmp:size=128m --user 1654:1654 \
      --mount "type=bind,src=$test_root/$directory,dst=/tests,readonly" --workdir /tests "$runtime" "$assembly" > "$stage/linux-$directory.log" 2>&1 || { tail -n 10 "$stage/linux-$directory.log"; exit 1; }
    tail -n 1 "$stage/linux-$directory.log"
  done
  if [[ $("${compose[@]}" exec -T postgres psql -U hifc_owner -d postgres -Atc "SELECT count(*) FROM pg_database WHERE datname='hifc_web_test'") == 0 ]]; then
    "${compose[@]}" exec -T postgres createdb -U hifc_owner -O hifc_web hifc_web_test
  fi
  database_run > "$stage/linux-database-tests.log" 2>&1 || { tail -n 12 "$stage/linux-database-tests.log"; exit 1; }
  tail -n 2 "$stage/linux-database-tests.log"
  database_run --write-persistence /validation/database-restart.json
else
  database_run --verify-persistence /validation/database-restart.json
fi
printf 'HIFC_RUNTIME_VERIFIED %s\n' "$mode"
