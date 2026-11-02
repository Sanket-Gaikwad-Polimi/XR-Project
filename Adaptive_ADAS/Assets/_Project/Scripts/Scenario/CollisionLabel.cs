// CollisionLabel.cs
// Put this on an object (or its parent) to give it a readable name for collision
// logging: "Lead", "Barrier", "Post", "Ground", ...
// EgoCarController looks for it when the car body hits something, so the logs say
// WHAT was hit (a crash into the lead car is a study measure; hitting a post is not).

using UnityEngine;

namespace AdaptiveADAS.Scenario
{
    public class CollisionLabel : MonoBehaviour
    {
        [Tooltip("Category written to the log, e.g. Lead, Barrier, Post, Ground.")]
        public string label = "Other";
    }
}
