# Audio Roadmap

Audio follows the usable-engine and Studio vertical; it does not weaken the
v14 video/media gates.

1. Add portable IDs, model, JSON validation, documentation and capability truth.
2. Compile the audio DAG into a deterministic physical plan with fan-out,
   latency accumulation and format-conversion decisions.
3. Run generated tone/silence through pooled blocks, buses, independent bounded
   sinks and transactional graph replacement. Pooled source-to-bus execution and
   transactional plan replacement are implemented; bounded independent sinks remain.
4. Add gain, mute, pan, polarity, channel mapping, mixing, meters and fixed
   delay with deterministic tests. Gain, mute, pan, polarity, mixing, meters and
   a one-quantum fixed delay are implemented; channel mapping remains pending.
5. After the Studio runtime vertical is accepted, expose sources, Program Bus,
   gain, mute, meters and video-output route assignment.

The portable test set must pass on Windows and Linux. Native adapters start
only after the portable runtime proves bounded ownership and a concrete platform
capability is available.
