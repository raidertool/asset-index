# ARC Raiders mapping

Tested with Steam build `25163933`, manifest `4625131891870974441`.

The supplied mapping conflated a class and struct named `AISensingStatusTransition`.
The struct now uses CUE4Parse's `AISensingStatusTransitionStruct` alias; its single
mapped reference was updated. The class has three fields and its correct parent,
verified from this build's script cache. Decode tests cover both layouts.

Submit mapping updates by PR and run the [offline tests](../CONTRIBUTING.md#test).
