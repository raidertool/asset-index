# ARC Raiders mapping

Target: Steam build `25163933`, manifest `4625131891870974441`.

The supplied `AISensingStatusTransition` struct uses CUE4Parse's explicit
`AISensingStatusTransitionStruct` alias; its mapped reference was updated.
The same-named native class has no verified layout. Its unsupported replacement
schema was removed; class discovery reports a blocking missing-schema error.
Tests cover struct decoding and explicit class failure.

Submit mapping updates by PR and run the [offline tests](../CONTRIBUTING.md#test).
