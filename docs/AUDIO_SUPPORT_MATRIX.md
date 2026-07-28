# Audio Support Matrix

| Capability | Model | Portable runtime | Product availability |
|---|---:|---:|---|
| Generated tone | Implemented | Implemented through pooled source-to-bus processing | Unavailable until runtime proof |
| Silence source | Implemented | Implemented through pooled source-to-bus processing | Unavailable until runtime proof |
| Memory sink | Planned | No | Test/debug only initially |
| Gain/mute/pan/polarity/mix/meter/fixed delay | Implemented | Implemented for deterministic bus processing | Unavailable until runtime proof |
| Channel mapper | Declared | No | Unavailable until conversion semantics are implemented |
| Physical input/loopback | Declared | No | Unavailable: platform adapter absent |
| Application capture | Declared | No | Unavailable: platform adapter absent |
| Audio file/network/Remote Scene | Declared | No | Unavailable: product path absent |
| Audio mux/encode | Route model only | No | Unavailable: no approved media path |
| Dynamics, AEC, plugins, MIDI | No product runtime | No | Deferred |

Only a capability with a passed backend/runtime proof may be exposed as
available. Model serialization alone is never capability evidence.
