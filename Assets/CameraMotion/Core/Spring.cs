using System;
using UnityEngine;

namespace Rift.CameraMotion
{
    [Serializable]
    public struct SpringSettings
    {
        [Tooltip("Частота колебаний, Гц. Больше — пружина жёстче и возвращается быстрее.")]
        [Min(0.1f)] public float Frequency;

        [Tooltip("Затухание. 1 — без перелёта, меньше 1 — с «отскоком».")]
        [Range(0f, 2f)] public float Damping;

        public SpringSettings(float frequency, float damping)
        {
            Frequency = frequency;
            Damping = damping;
        }
    }

    /// <summary>
    /// Пружина для Vector3. Толчок задаётся скоростью (AddImpulse), пружина сама возвращает значение к цели.
    /// Шаг интегрирования дробится, чтобы жёсткие пружины не разлетались при низком FPS.
    /// </summary>
    [Serializable]
    public struct Spring3
    {
        private const float MaxStep = 1f / 240f;

        public Vector3 Value;
        public Vector3 Velocity;

        public void AddImpulse(Vector3 velocity) => Velocity += velocity;

        public void Reset()
        {
            Value = Vector3.zero;
            Velocity = Vector3.zero;
        }

        public Vector3 Update(Vector3 target, in SpringSettings settings, float deltaTime)
        {
            if (deltaTime <= 0f) return Value;

            float omega = 2f * Mathf.PI * settings.Frequency;
            float k = omega * omega;
            float c = 2f * settings.Damping * omega;

            int steps = Mathf.CeilToInt(deltaTime / MaxStep);
            float dt = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 accel = k * (target - Value) - c * Velocity;
                Velocity += accel * dt;
                Value += Velocity * dt;
            }
            return Value;
        }
    }

    /// <summary>Та же пружина для одного float (FOV).</summary>
    [Serializable]
    public struct Spring1
    {
        private const float MaxStep = 1f / 240f;

        public float Value;
        public float Velocity;

        public void AddImpulse(float velocity) => Velocity += velocity;

        public void Reset()
        {
            Value = 0f;
            Velocity = 0f;
        }

        public float Update(float target, in SpringSettings settings, float deltaTime)
        {
            if (deltaTime <= 0f) return Value;

            float omega = 2f * Mathf.PI * settings.Frequency;
            float k = omega * omega;
            float c = 2f * settings.Damping * omega;

            int steps = Mathf.CeilToInt(deltaTime / MaxStep);
            float dt = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = k * (target - Value) - c * Velocity;
                Velocity += accel * dt;
                Value += Velocity * dt;
            }
            return Value;
        }
    }

    public static class Damp
    {
        /// <summary>Плавное приближение к цели, не зависящее от FPS. time — примерное время «доезда» (~95%).</summary>
        public static float Towards(float current, float target, float time, float deltaTime)
        {
            if (time <= 0f) return target;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-3f * deltaTime / time));
        }

        public static Vector3 Towards(Vector3 current, Vector3 target, float time, float deltaTime)
        {
            if (time <= 0f) return target;
            return Vector3.Lerp(current, target, 1f - Mathf.Exp(-3f * deltaTime / time));
        }
    }
}

namespace Rift.CameraMotion
{
    public static class SpringKick
    {
        /// <summary>
        /// Пересчитать желаемую пиковую амплитуду толчка в начальную скорость пружины.
        /// Так в инспекторе задаются понятные градусы/метры, а не «скорость пружины».
        /// </summary>
        public static float VelocityForPeak(float peak, in SpringSettings s)
        {
            float omega = 2f * UnityEngine.Mathf.PI * s.Frequency;
            float z = UnityEngine.Mathf.Clamp(s.Damping, 0f, 0.999f);
            float wd = UnityEngine.Mathf.Sqrt(1f - z * z);
            // Пик затухающего осциллятора с нулевым стартом: v/ω * exp(-ζ/√(1-ζ²) * atan(√(1-ζ²)/ζ))
            float tPeakFactor = z < 0.0001f ? 1f : UnityEngine.Mathf.Exp(-z / wd * UnityEngine.Mathf.Atan2(wd, z));
            return peak * omega / tPeakFactor;
        }

        public static UnityEngine.Vector3 VelocityForPeak(UnityEngine.Vector3 peak, in SpringSettings s) =>
            peak * VelocityForPeak(1f, s);
    }
}
