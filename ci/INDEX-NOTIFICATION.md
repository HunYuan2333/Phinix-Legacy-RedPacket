# Index notification after release

A successful main release starts a separate notification job. It dispatches
`HunYuan2333/Phinix-Plugin-Index/plugin-source-updates.yml` on main with
`check_only=false`, waits for that scan, and retries a failed scan with a fresh
main snapshot (at most three dispatches, twenty minutes total).

Configure repository secret `INDEX_UPDATE_TOKEN`: a maintainer-owned fine-grained
PAT restricted to Phinix-Plugin-Index, Actions read/write. The token owner must
already satisfy Index maintainer checks. Never reuse BUILD_REFERENCES_TOKEN.
Third-party authors are not given this credential; scheduled scanning remains
available for every approved source without a notification integration.

Missing credentials, API errors or exhausted retries emit a warning; they do not
unpublish or rebuild the successful Release. Scan success means discovery finished,
not that this plugin was admitted or the catalog is visible. Inspect the Index
scan report and controlled publication run for that result. Existing approval,
source, digest, version and immutable publication checks remain authoritative.

The Index serializes metadata writers using queue:max. This bounded queue avoids
replacing pending publication runs; queued snapshots may still become obsolete.
The notifier starts a new run rather than rerunning the obsolete SHA. Hourly scans
remain a fallback and GitHub does not guarantee their exact start time.

Only main pushes run release and notification jobs. Dev and PR do neither.
