# TASK-168 — Planetary Globe & Geodesic Surface Topology

The live planet now has one shared spherical address model. Surface gameplay remains a bounded floating-origin tangent patch; logical east/north coordinates are converted into normalized latitude/longitude on the planet sphere.

Orbit and interplanetary contexts prepare exactly one detailed current-planet globe from the verified six-face cube-sphere mesh builder (17×17 vertices per face), with bounded atmosphere/water/cloud presentation. Other bodies remain proxy/marker/statistical representations.

The 840 m distant surface proxy receives exact sphere-tangent sag derived from the current planet radius. Collision, navigation and the 25-chunk/9-collision gameplay streamer remain local and unchanged.

Planet map distances use great-circle distance and display latitude/longitude. Persistence remains logical X/Z and requires no schema migration.

TASK-168 deliberately does not introduce radial player gravity, physical cube-face collision streaming, or cube-face transitions. Those are a later physics/topology layer; this task establishes the global geographic/orbital contract they can consume.
