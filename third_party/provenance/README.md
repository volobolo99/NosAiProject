# Third-Party Provenance

Every source copied into `third_party/sources/` must have provenance recorded here or in a dedicated manifest.

Minimum record:

- upstream repository
- upstream path
- upstream revision/commit when known
- license
- original copyright holder when available
- reason for inclusion
- whether copied verbatim or modified
- target NosAi component, if integrated
- test status

Do not remove provenance records when code is later adapted.

## Current policy

Cursor and Claude are explicitly allowed to copy/adapt licensed code from the local vault, subject to the source license and the project architecture. GPL/LGPL/MIT sources are not blocked merely because of their license.

The vault is permanent project evidence: do not delete its files automatically.
