# Canonical content is signed with one self-signed certificate, pinned per flavor

One self-signed certificate, held by the maintainer, signs the catalog and every canonical `.mwm`/`.mwassets`. Each flavor pins that certificate's thumbprint. There is no CA, no chain validation and no CRL/OCSP: rotating means generating a new certificate and shipping an app update that re-pins it, and for a self-signed certificate that rotation *is* revocation. Content that is unsigned or from an unrecognized publisher is warned about but allowed. Verification is deliberately light because modules and asset packs carry no executable code: they're declarative data interpreted by engine code that already shipped.

## Consequences

- A catalog install is verified by hash only. The signed catalog makes its listed SHA-256 trustworthy, so no per-package signature check is needed.
- The private key is a single point of failure for all canonical trust. `docs/release.md` covers handling it.
- A community publisher can use their own self-signed certificate without a format change. They get the warn-but-allow prompt.
