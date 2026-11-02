// FitTextHeight.cs
// Unity's legacy 3D text (TextMesh) has font-dependent sizes. This scales the text
// once at start so that its letters are targetHeight metres tall, whatever the font.

using UnityEngine;

namespace AdaptiveADAS.HMI
{
    [RequireComponent(typeof(TextMesh))]
    public class FitTextHeight : MonoBehaviour
    {
        [Tooltip("Height of the text in metres.")]
        public float targetHeight = 0.05f;

        private void Start()
        {
            float h = GetComponent<MeshRenderer>().bounds.size.y;
            if (h > 0.0001f) transform.localScale *= targetHeight / h;
        }
    }
}
