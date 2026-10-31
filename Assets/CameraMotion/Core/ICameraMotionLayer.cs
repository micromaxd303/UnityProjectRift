namespace Rift.CameraMotion
{
    /// <summary>
    /// Слой анимации камеры. Компонент с этим интерфейсом кладётся на тот же объект,
    /// что и CinemachineCamera с PlayerCameraMotion, и подхватывается автоматически.
    /// </summary>
    public interface ICameraMotionLayer
    {
        /// <summary>Выключенный слой не вызывается.</summary>
        bool IsLayerActive { get; }

        /// <summary>Подчиняется ли слой общей «интенсивности» (опция уменьшения покачивания).</summary>
        bool AffectedByIntensity { get; }

        /// <summary>Обновить слой и вернуть его смещение за этот кадр.</summary>
        CameraOffset Evaluate(float deltaTime);

        /// <summary>Сбросить всё накопленное состояние (телепорт, респаун, смена камеры).</summary>
        void ResetLayer();
    }
}
