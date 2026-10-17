"""Verify the deployed HTTPS endpoint with its specific private CA and hostname."""
import argparse
import hashlib
import json
import pathlib
import ssl
import urllib.error
import urllib.request
from datetime import datetime, timezone

parser = argparse.ArgumentParser()
parser.add_argument("--phase", choices=("before", "after"), default="before")
parser.add_argument("--accounts-enabled", action="store_true")
parser.add_argument("--expected-revision")
args = parser.parse_args()
web = pathlib.Path(__file__).resolve().parent.parent
ca = web / ".local/certificates/hifc-web-root-ca.crt"
proof = web / ".local/deployment-proof"
proof.mkdir(parents=True, exist_ok=True)
context = ssl.create_default_context(cafile=str(ca))
assert context.check_hostname and context.verify_mode == ssl.CERT_REQUIRED
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
base = "https://192.168.6.45:8443"

def request(path, data=None):
    operation = urllib.request.Request(base + path, data=data, headers={"Content-Type": "application/json"})
    try:
        with opener.open(operation, timeout=15) as reply:
            return reply.status, reply.read()
    except urllib.error.HTTPError as reply:
        return reply.code, reply.read()

status, body = request("/health/ready")
ready = json.loads(body)
assert status == 200 and ready["state"] == "ready" and ready["database"] == "connected", "Database readiness failed"
assert ready["authenticationConfigured"] is args.accounts_enabled, "Authentication mode differs from the requested release"
status, body = request("/health/live")
live = json.loads(body)
assert status == 200 and live["authenticationConfigured"] is args.accounts_enabled
if args.expected_revision:
    assert live["buildRevision"] == args.expected_revision, "Running source revision differs from the candidate"
status, body = request("/api/v1/compatibility")
compatibility = json.loads(body)
assert status == 200 and compatibility["protocolMajor"] == 1 and compatibility["ruleVersion"] == "reference-0.4.3-web1", "Protocol compatibility failed"
signing_key = compatibility["signingKey"]
assert signing_key["modulus"] and signing_key["exponent"], "Public signing key is absent"
status, body = request("/")
assert status == 200 and ("下载 Web 插件与安装器" if args.accounts_enabled else "服务器已启动") in body.decode("utf-8"), "Deployed entrance page failed"
status, body = request("/api/v1/accounts/register", b"{}")
registration_status = status
if args.accounts_enabled:
    assert status == 400 and json.loads(body)["code"] == "REQUEST_REJECTED", "Enabled registration did not validate an empty request"
    status, body = request("/api/v1/me")
    assert status == 401, "Anonymous account access was accepted"
    status, body = request("/admin/login")
    assert status == 200 and 'name="csrf"' in body.decode("utf-8"), "Administrator entrance is missing"
    status, body = request("/downloads/HIFC-0.5.0-Web.zip")
    assert status == 200 and body[:2] == b"PK", "Client package is missing"
    client_package_hash = hashlib.sha256(body).hexdigest()
    latest = json.loads((web / ".local/latest-client-release.json").read_text(encoding="utf-8-sig"))
    assert client_package_hash == latest["sha256"], "Downloaded client differs from the validated package"
else:
    assert status == 503 and json.loads(body)["code"] == "AUTHENTICATION_NOT_CONFIGURED", "Deferred registration is open"
certificate_der = ssl.PEM_cert_to_DER_cert(ca.read_text(encoding="ascii"))
record = {
    "verifiedUtc": datetime.now(timezone.utc).isoformat(), "phase": args.phase, "baseUrl": base,
    "tls": {"hostnameValidated": True, "certificateChainRequired": True,
            "rootCertificateSha256": hashlib.sha256(certificate_der).hexdigest(), "client": ssl.OPENSSL_VERSION},
    "ready": ready, "protocolMajor": compatibility["protocolMajor"], "ruleVersion": compatibility["ruleVersion"],
    "publicSigningKeySha256": hashlib.sha256(json.dumps(signing_key, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
    "entrancePageHttpStatus": 200, "registrationHttpStatus": registration_status, "authenticationConfigured": args.accounts_enabled,
    "buildRevision": live["buildRevision"],
}
if args.accounts_enabled:
    record["clientPackageSha256"] = client_package_hash
if args.phase == "after":
    previous = json.loads((proof / "https-before.json").read_text(encoding="utf-8"))
    assert record["tls"]["rootCertificateSha256"] == previous["tls"]["rootCertificateSha256"], "Private CA identity changed after recreation"
    assert record["publicSigningKeySha256"] == previous["publicSigningKeySha256"], "Signing identity changed after recreation"
    record["caAndSigningIdentityPreserved"] = True
(proof / ("https-" + args.phase + ".json")).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS HTTPS certificate chain and hostname; ready=200, compatibility=200, entrance=200; authentication=" + str(args.accounts_enabled))
if args.accounts_enabled:
    print("PASS real client download hash, anonymous denial, registration validation and administrator entrance")
if args.phase == "after":
    print("PASS container recreation preserves CA and public signing identity")
