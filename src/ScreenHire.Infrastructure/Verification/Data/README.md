# Verification data files

`disposable-email-domains.txt` is the blocklist from
[disposable-email-domains/disposable-email-domains](https://github.com/disposable-email-domains/disposable-email-domains)
(CC0-1.0 per the repository's LICENSE file, copied here as `disposable-email-domains.LICENSE.txt`), taken at commit `b63df8b431f37ef0b22524f9cae2e86c7114a9de` on 2026-10-20. One
domain per line. `DisposableEmailDomains` embeds it; `ScreenHire:Verification:Identity:ExtraDisposableDomains` adds more.

To refresh it, download `disposable_email_blocklist.conf` from that repository over this file and update the commit
above.
