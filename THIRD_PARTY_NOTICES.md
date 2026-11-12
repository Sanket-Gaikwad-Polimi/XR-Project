# Third-party components

| Component | Where | Licence | Notes |
|---|---|---|---|
| NetMQ 4.0.0.1 | `Adaptive_ADAS/Assets/Plugins/NetMQ/NetMQ.dll` | LGPL-3.0 (text in the same folder, with GPL-3.0) | Unmodified binary from NuGet. Source: https://github.com/zeromq/netmq |
| AsyncIO 0.1.69 | `Adaptive_ADAS/Assets/Plugins/NetMQ/AsyncIO.dll` | MPL-2.0 (text in the same folder) | Unmodified binary from NuGet. Source: https://github.com/somdoron/AsyncIO |
| AprilTag tag36h11 images (IDs 0-7) | `Docs/markers/apriltag_tag36h11_50mm.pdf` | BSD-2-Clause (text in `Docs/markers/`), Copyright (c) 2013-2016, The Regents of The University of Michigan | Tag patterns from https://github.com/AprilRobotics/apriltag-imgs, scaled up and laid out for printing |
| Liberation Sans Bold (glyph shapes) | `Adaptive_ADAS/Assets/_Project/Textures/SpeedSignDigits.png` | SIL Open Font License 1.1 | Digits 0-9 rendered into a texture by `Docs/tools/make_sign_digits.js` |

**Used as a reference only, no code copied:**
- Pupil Labs **hmd-eyes** (LGPL-3.0, https://github.com/pupil-labs/hmd-eyes): the way it connects to Pupil Capture with NetMQ.
- Pupil Labs **Pupil** source (LGPL-3.0, https://github.com/pupil-labs/pupil): the message topics and fields.
