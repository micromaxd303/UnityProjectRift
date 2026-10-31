using System;
using UnityEngine;

namespace Rift.CameraMotion
{
    /// <summary>
    /// Смещение камеры, которое добавляет один слой анимации.
    /// Position — в локальных осях камеры (x вправо, y вверх, z вперёд), метры.
    /// Rotation — углы Эйлера в градусах (x — тангаж, отрицательный = вверх; y — рыскание; z — крен).
    /// Fov — добавка к полю зрения в градусах.
    /// </summary>
    [Serializable]
    public struct CameraOffset
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public float Fov;

        public static readonly CameraOffset Zero = default;

        public CameraOffset(Vector3 position, Vector3 rotation, float fov)
        {
            Position = position;
            Rotation = rotation;
            Fov = fov;
        }

        public static CameraOffset operator +(CameraOffset a, CameraOffset b) =>
            new CameraOffset(a.Position + b.Position, a.Rotation + b.Rotation, a.Fov + b.Fov);

        public static CameraOffset operator *(CameraOffset a, float k) =>
            new CameraOffset(a.Position * k, a.Rotation * k, a.Fov * k);
    }
}
