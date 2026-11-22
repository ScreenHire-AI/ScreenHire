import hashlib
import hmac
import time


def verify_screenhire_signature(raw_body: bytes, header: str, secret: str, tolerance_seconds: int = 300) -> bool:
    """Checks a ScreenHire webhook before you trust it.

    raw_body is the request body exactly as it arrived, before any JSON parsing; header is the ScreenHire-Signature
    header; secret is your endpoint's signing secret (whsec_...). A request more than tolerance_seconds old is refused,
    so a captured delivery cannot be sent again later.
    """
    parts = dict(part.split("=", 1) for part in header.split(",") if "=" in part)
    timestamp, given = parts.get("t", ""), parts.get("v1", "")
    if not timestamp.isdigit() or not given or abs(time.time() - int(timestamp)) > tolerance_seconds:
        return False

    expected = hmac.new(secret.encode(), timestamp.encode() + b"." + raw_body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, given.lower())
