// RearViewMirror.cs
// A real (render-texture) rear-view mirror:
//   - a second camera sits at the mirror and looks BACKWARDS,
//   - it renders into a small texture (default 320 x 100 pixels: cheap on an RTX 2050),
//   - the texture is shown on the mirror surface, flipped left-right like a real mirror.
// Shadows, post-processing and anti-aliasing are switched off for this camera to keep it
// cheap. A camera at the mirror is an approximation: a real mirror shows the view
// reflected from the driver's eye position, which differs slightly. Our choice.

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AdaptiveADAS.HMI
{
    public class RearViewMirror : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public Camera mirrorCamera;
        [Tooltip("The mirror glass (a flat box/quad with an Unlit material).")]
        public Renderer mirrorSurface;

        [Header("Settings")]
        [Tooltip("Texture width in pixels. Lower = cheaper, blurrier. Our choice.")]
        public int textureWidth = 320;
        [Tooltip("Texture height in pixels. Width/height should match the mirror's shape (0.27 x 0.08 m -> ~3.3).")]
        public int textureHeight = 100;
        [Tooltip("Vertical field of view of the mirror image (deg). Our choice (real mirror coverage: TO FIND).")]
        public float mirrorVerticalFov = 12f;

        private RenderTexture texture;

        private void Awake()
        {
            texture = new RenderTexture(textureWidth, textureHeight, 24) { name = "RearViewMirrorRT" };
            texture.Create();

            mirrorCamera.targetTexture = texture;      // camera aspect follows the texture
            mirrorCamera.fieldOfView = mirrorVerticalFov;
            mirrorCamera.depth = -10;                  // render before the main camera

            UniversalAdditionalCameraData data = mirrorCamera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            // Show the texture on the mirror, flipped horizontally (x scale -1, offset 1).
            Material m = mirrorSurface.material;       // per-object copy of the material
            m.SetTexture("_BaseMap", texture);
            m.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
            m.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
        }

        private void OnDestroy()
        {
            if (mirrorCamera != null) mirrorCamera.targetTexture = null;
            if (texture != null) texture.Release();
        }
    }
}
