import { createHmac, timingSafeEqual } from 'node:crypto';

/**
 * Checks a ScreenHire webhook before you trust it. `rawBody` is the request body exactly as it arrived (a string,
 * before any JSON parsing), `header` the ScreenHire-Signature header, and `secret` your endpoint's signing secret
 * (shws_...). A request more than `toleranceSeconds` old is refused, so a captured delivery cannot be sent again later.
 */
export function verifyScreenHireSignature(rawBody, header, secret, toleranceSeconds = 300, nowSeconds = Date.now() / 1000) {
  const parts = Object.fromEntries(header.split(',').map((part) => part.split('=')));
  const timestamp = Number(parts.t);
  if (!Number.isInteger(timestamp) || !parts.v1 || Math.abs(nowSeconds - timestamp) > toleranceSeconds) {
    return false;
  }

  const expected = createHmac('sha256', secret).update(`${parts.t}.${rawBody}`).digest();
  const given = Buffer.from(parts.v1, 'hex');
  return given.length === expected.length && timingSafeEqual(given, expected);
}
