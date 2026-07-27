# Audio Support Matrix

| Capability | Model | Portable runtime | Product availability |
|---|---:|---:|---|
| Generated tone | Planned | Planned | Unavailable until runtime proof |
| Silence source | Planned | Planned | Unavailable until runtime proof |
| Memory sink | Planned | Planned | Test/debug only initially |
| Gain/mute/pan/mix/meter | Planned | Planned | Unavailable until runtime proof |
| Physical input/loopback | Declared | No | Unavailable: platform adapter absent |
| Application capture | Declared | No | Unavailable: platform adapter absent |
| Audio file/network/Remote Scene | Declared | No | Unavailable: product path absent |
| Audio mux/encode | Route model only | No | Unavailable: no approved media path |
| Dynamics, AEC, plugins, MIDI | No product runtime | No | Deferred |

Only a capability with a passed backend/runtime proof may be exposed as
available. Model serialization alone is never capability evidence.
