# Material icons

Two compiled SM5 graphs have explicit renderers:

- `M_CharacterColorSchemeIcon` instances: three color bands, metallic lighting,
  bevels and named overrides.
- `M_UI_Icon_Extractor` (Close Scrutiny): texture alpha plus three animated
  pulses. Its catalog image fixes the captured View-buffer input to **0** and
  uses the original material's defaults. Material instances are unsupported.

Exact parent-package hashes pin the graphs, defaults and bindings. Changed
inputs, unsupported overrides or invalid arithmetic produce diagnostics.

## Standalone output

- 256 × 256 pixels; normalized UVs at pixel centers; mip 0; bilinear filtering.
- Color-scheme background wraps and bevel clamps. Close Scrutiny preserves its
  texture's address modes; its native-size samples stay inside the texture.
- White vertex tint; no widget effects or extra contrast/gamma adjustment.
- sRGB PNG with straight alpha, computed from the material's RGB and opacity.

These conditions do not reproduce arbitrary widgets, engine filtering or SM6
GPU output. The Close Scrutiny time field's name was stripped from the shader;
we do not call it GameTime, RealTime or seconds.

Tests use synthetic images and independent compiled-instruction arithmetic
fixtures. Other materials remain unsupported; a referenced ingredient is never
substituted for the finished icon.
