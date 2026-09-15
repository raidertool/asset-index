# Discovery

The catalog is a projection of client evidence. Asset IDs do not identify every
string, texture, UI object, quest node, or store entry.

## Follow evidence

- Inventory registered and unregistered packages; report unknown classes and
  decoding failures.
- Read serialized properties, nested structures/arrays, templates, string
  tables and localization resources. A short list of display fields is not a
  complete resource inventory.
- Preserve the source object, property path, reference kind and text key.
  Template inheritance, containment, rewards and presentation links have
  different meanings.
- Associate a name or icon with an ID through its explicit identity and
  presentation fields. A reference to another asset does not transfer that
  asset's name. Filename similarity is only a discovery lead.
- Keep unassociated strings and textures discoverable. Do not invent asset IDs
  for them or silently discard them because the catalog has no matching row.
- Follow material inputs as evidence; a component texture is not a rendered
  material icon.

The current extractor resolves known catalog fields. The wider package/resource
inventory was exercised by a separate audit and is not yet part of its public
output. Keep that coverage gap visible when extending the extractor.

## Verify a contribution

Use a small fixture for the actual property/reference shape. Check the known
asset and neighboring cases, preserve null versus failed reads, and explain
any changed ID, name, locale or image association. Historical snapshots find
regressions; current client evidence determines the result.
