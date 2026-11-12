// PupilNetMQ.cs
// Start-up and shut-down of the NetMQ library (ZeroMQ for .NET), shared by PupilGazeSource
// and FakePupilServer. Same steps as Pupil Labs' hmd-eyes Unity plugin (Request.cs and
// NetMQCleanup.cs, read 2026-11-12):
//   - AsyncIO.ForceDotNet.Force() before the first socket: makes NetMQ use plain .NET sockets
//     (hmd-eyes calls it before every connection; why it is needed in Unity's Mono: TO VERIFY,
//     we simply follow hmd-eyes),
//   - NetMQConfig.Cleanup(block: false) when the last user stops: without it the Unity editor
//     can hang when Play stops or Unity quits (a known NetMQ-in-Unity problem; hmd-eyes does the same).
// Every socket must be closed with Linger = 0 BEFORE Cleanup (see CloseSocket).

using System;
using NetMQ;
using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public static class PupilNetMQ
    {
        private static int users;

        /// <summary>Call once per component that uses NetMQ, before its first socket.</summary>
        public static void Acquire()
        {
            if (users == 0) AsyncIO.ForceDotNet.Force();
            users++;
        }

        /// <summary>Call when the component has closed all its sockets.</summary>
        public static void Release()
        {
            if (users <= 0) return;
            users--;
            if (users == 0)
            {
                try { NetMQConfig.Cleanup(false); }
                catch (Exception e) { Debug.LogWarning("[Pupil] NetMQ cleanup: " + e.Message); }
            }
        }

        /// <summary>Closes a socket at once, dropping unsent messages (Linger = 0). Null is fine.</summary>
        public static void CloseSocket(NetMQSocket s)
        {
            if (s == null) return;
            try
            {
                s.Options.Linger = TimeSpan.Zero;
                s.Dispose();
            }
            catch (Exception e) { Debug.LogWarning("[Pupil] closing a socket: " + e.Message); }
        }
    }
}
