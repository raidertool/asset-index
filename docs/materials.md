# Material icons

`MaterialIcons.cs` renders `M_CharacterColorSchemeIcon` instances from the
verified SM5 shader's arithmetic and named parameter bindings. An exact parent
package hash prevents using that formula after the graph changes.

## Output conditions

- 256 × 256 pixels, normalized UVs sampled at pixel centers.
- Mip 0 with bilinear filtering; background wraps, bevel clamps.
- White vertex tint, no widget effects or extra contrast/gamma adjustment.
- sRGB PNG with straight alpha. The material's selection-color override applies.

These define a standalone catalog image. They do not reproduce arbitrary
widget sizes, display settings, texture-group filtering, or SM6 GPU output.

The renderer reads the parent's serialized texture bindings and the instance's
named overrides. It combines three diagonal color bands with metallic lighting,
two bevel samples, and background opacity. Unsupported parents, permutations,
overrides, changed inputs, or invalid arithmetic produce explicit diagnostics.

Other materials remain unsupported. Their referenced textures stay discoverable;
the extractor does not substitute an ingredient for a finished icon. Test new
renderers with synthetic inputs and independently verify their compiled graph.
