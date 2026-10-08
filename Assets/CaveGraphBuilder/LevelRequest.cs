using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Этап 0 пайплайна: что игра просит у генератора уровня.
/// Пока используется только генератором пещер Инкубатора.
/// Из запроса через LevelInfo собираются требования к графу регионов.
/// </summary>
[Serializable]
public class LevelRequest
{
    [Serializable]
    public class SideMission
    {
        [Tooltip("Идентификатор квеста (Доктор, Инженер). Пусто — подставится side_N.")]
        public string Id = "";
        public LevelInfo.SideMissionKind Kind;
    }

    [Tooltip("Базовый сид уровня. Если уровень не прошёл проверку, генератор берёт Seed + 1, + 2, ... " +
             "и пишет итоговый сид в LevelContext.Seed.")]
    public int Seed = 2;

    [Tooltip("Номер сектора: от него зависят открытые слоты модификаторов.")]
    [Min(1)] public int Sector = 1;

    [Tooltip("Сложность. Генератор пещер её пока не использует, она передаётся в LevelContext для игры.")]
    [Min(1)] public int Difficulty = 1;

    public LevelInfo.MissionType MainMission = LevelInfo.MissionType.Incubators;
    public List<SideMission> SideMissions = new();

    [Tooltip("Идентификаторы модификаторов (хорошие и плохие). Генератор пещер их пока не использует.")]
    public List<string> Modifiers = new();

    public string SideMissionId(int index)
    {
        string id = SideMissions[index].Id;
        return string.IsNullOrEmpty(id) ? $"side_{index}" : id;
    }

    /// <summary>Требования к графу: основная миссия задаёт скелет, дополнительные добавляют регионы.</summary>
    public LevelInfo.LevelRequirements ToRequirements()
    {
        var main = LevelInfo.MissionRequirements.For(MainMission);
        var side = new List<LevelInfo.SideMissionRequirements>();
        for (int i = 0; i < SideMissions.Count; i++)
            side.Add(LevelInfo.SideMissionRequirements.For(SideMissionId(i), SideMissions[i].Kind));
        return LevelInfo.LevelRequirements.Merge(main, side);
    }

    /// <summary>Копия, чтобы правки в инспекторе не меняли запрос уже построенного уровня.</summary>
    public LevelRequest Clone()
    {
        var copy = (LevelRequest)MemberwiseClone();
        copy.SideMissions = new List<SideMission>();
        foreach (var m in SideMissions)
            copy.SideMissions.Add(new SideMission { Id = m.Id, Kind = m.Kind });
        copy.Modifiers = new List<string>(Modifiers);
        return copy;
    }
}
