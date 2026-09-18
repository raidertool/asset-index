# ARC Raiders mapping

Target: Steam build `25163933`, manifest `4625131891870974441`.

The supplied `AISensingStatusTransition` struct uses CUE4Parse's explicit
`AISensingStatusTransitionStruct` alias; its mapped reference was updated.
The same-named native class has no verified layout and is not decoded. Verified
class-family evidence can exclude unrelated exports from the catalog; missing
layouts for catalog dependencies still block extraction. Tests cover struct
decoding and these boundaries.

Submit mapping updates by PR and run the [offline tests](../CONTRIBUTING.md#test).
