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
assert ready["authenticationConfigured"] is False, "Unapproved authentication configuration"
status, body = request("/api/v1/compatibility")
compatibility = json.loads(body)
assert status == 200 and compatibility["protocolMajor"] == 1 and compatibility["ruleVersion"] == "reference-0.4.3-web1", "Protocol compatibility failed"
signing_key = compatibility["signingKey"]
assert signing_key["modulus"] and signing_key["exponent"], "Public signing key is absent"
status, body = request("/")
assert status == 200 and "服务器已启动" in body.decode("utf-8"), "Deployed status page failed"
status, body = request("/api/v1/accounts/register", b"{}")
assert status == 503 and json.loads(body)["code"] == "AUTHENTICATION_NOT_CONFIGURED", "Deferred registration is open"
certificate_der = ssl.PEM_cert_to_DER_cert(ca.read_text(encoding="ascii"))
record = {
    "verifiedUtc": datetime.now(timezone.utc).isoformat(), "phase": args.phase, "baseUrl": base,
    "tls": {"hostnameValidated": True, "certificateChainRequired": True,
            "rootCertificateSha256": hashlib.sha256(certificate_der).hexdigest(), "client": ssl.OPENSSL_VERSION},
    "ready": ready, "protocolMajor": compatibility["protocolMajor"], "ruleVersion": compatibility["ruleVersion"],
    "publicSigningKeySha256": hashlib.sha256(json.dumps(signing_key, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
    "statusPageHttpStatus": 200, "registrationHttpStatus": 503, "authenticationConfigured": False,
}
if args.phase == "after":
    previous = json.loads((proof / "https-before.json").read_text(encoding="utf-8"))
    assert record["tls"]["rootCertificateSha256"] == previous["tls"]["rootCertificateSha256"], "Private CA identity changed after recreation"
    assert record["publicSigningKeySha256"] == previous["publicSigningKeySha256"], "Signing identity changed after recreation"
    record["caAndSigningIdentityPreserved"] = True
(proof / ("https-" + args.phase + ".json")).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS HTTPS certificate chain and hostname; ready=200, compatibility=200, page=200, deferred registration=503")
if args.phase == "after":
    print("PASS container recreation preserves CA and public signing identity")
