# IDS 1.0 schema, for offline validation

`test_ids_contract.py` validates every `shared/ifc/ids/*.ids` against the
buildingSMART IDS 1.0 XML schema without touching the network. The files here
are unmodified copies; the test maps the schema's `schemaLocation` URLs to them.

| File | Source | Licence |
|---|---|---|
| `ids.xsd` | buildingSMART IDS v1.0.0, `Development/ids.xsd` at tag `v1.0.0` (commit `1effec6f419798ce09617416d258a35bdc58320a`), github.com/buildingSMART/IDS. Byte-identical to the published https://standards.buildingsmart.org/IDS/1.0/ids.xsd (SHA-256 `8975dc18bd18f08a345a430a22bf317a54d94671b7db01400042fa43b6c0d1f3`, checked 2026-10-02). | © buildingSMART International Ltd., CC BY-ND 4.0 (https://creativecommons.org/licenses/by-nd/4.0/) |
| `xml.xsd` | http://www.w3.org/2001/xml.xsd | © W3C, W3C Software and Document License |
| `XMLSchema.xsd` | http://www.w3.org/2001/XMLSchema.xsd | © W3C, W3C Software and Document License |

`ids.xsd` also imports `http://www.w3.org/2001/XMLSchema-instance`, which is a
namespace document, not a schema; the test answers that import with an empty
schema for the namespace (the `xsi:` attributes are built into every validator).

To update: replace a file with the newer published copy, unmodified, and update
the commit / hash above.
