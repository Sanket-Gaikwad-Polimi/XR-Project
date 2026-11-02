// Speedometer.cs
// Digital speedometer on the instrument cluster: shows the ego car's speed in km/h
// (whole numbers), updated every rendered frame. Digital was chosen because a large
// number is easy to read in the greybox; an analog gauge can come in the visual pass.

using AdaptiveADAS.Vehicle;
using UnityEngine;

namespace AdaptiveADAS.HMI
{
    public class Speedometer : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public EgoCarController egoCar;
        public TextMesh speedText;

        private int lastShown = -1;

        private void Update()
        {
            if (egoCar == null || speedText == null) return;
            int kmh = Mathf.RoundToInt(Mathf.Abs(egoCar.ForwardSpeed) * 3.6f);
            if (kmh != lastShown)
            {
                speedText.text = kmh.ToString();
                lastShown = kmh;
            }
        }
    }
}
