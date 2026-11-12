// MouseGazeSource.cs
// Home-testing stand-in for an eye tracker: the MOUSE POINTER is the gaze point.
// Drive with the keyboard (left hand) and "look" with the mouse (right hand).
// While driving, the system cursor is hidden; in test mode GazeDebugView draws a small
// dot instead, so you can see where you are "looking".
// The gaze ray goes from the driver camera through the pointer. A pointer outside the
// Game window counts as looking off-screen (if Unity still reports the pointer position
// there: TO CHECK in the editor).

using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.Twin
{
    public class MouseGazeSource : GazeSource
    {
        [Tooltip("Camera whose view the pointer refers to (the driver camera).")]
        public Camera targetCamera;
        [Tooltip("Hide the system mouse cursor while the game window has focus.")]
        public bool hideCursor = true;

        public override string SourceName => "Mouse";

        public override bool TryGetGazeSample(out GazeSample sample)
        {
            sample = GazeSample.Invalid;
            if (Mouse.current == null || targetCamera == null) return false;
            Vector3 vp = targetCamera.ScreenToViewportPoint(Mouse.current.position.ReadValue());
            sample = GazeSample.FromViewport(targetCamera, new Vector2(vp.x, vp.y));
            return true;
        }

        private void Update()
        {
            // Re-applied every frame: Unity shows the cursor again after the window loses focus.
            if (hideCursor && Application.isFocused) Cursor.visible = false;
        }

        private void OnDisable() => Cursor.visible = true;
    }
}
