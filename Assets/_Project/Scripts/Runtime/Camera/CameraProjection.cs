using UnityEngine;

namespace OrangeCarrrrr.Runtime
{
    /// <summary>
    /// The projection quirk every cameraman inherits, because it belongs to the
    /// renderer rather than to any one of them.
    ///
    /// <c>project_point</c> puts the vanishing point at 0.52 of the client height
    /// rather than 0.50, so the horizon sits slightly below centre. Reproduced as a
    /// shift of the projection matrix's y row.
    /// </summary>
    public static class CameraProjection
    {
        public const float VerticalCenterFraction = 0.52f;

        /// <summary>
        /// Moving the principal point down by 0.02 of the height moves the image
        /// down by 0.04 in NDC, which is a constant added to the projection's y row.
        /// Passing false restores Unity's own projection.
        /// </summary>
        public static void ApplyVerticalCenterShift(Camera camera, bool enabled)
        {
            if (camera == null) return;

            camera.ResetProjectionMatrix();
            if (!enabled) return;

            Matrix4x4 projection = camera.projectionMatrix;
            projection[1, 2] += (VerticalCenterFraction - 0.5f) * 2f;
            camera.projectionMatrix = projection;
        }
    }
}
