using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Контракт между миссиями и генераторами уровней (раздел GDD «Миссии и задания»).
/// Миссия описывает требования к графу регионов, генератор ветки строит уровень,
/// который им удовлетворяет. Контракт не зависит от способа генерации:
/// пещеры Инкубатора и генераторы других измерений читают одни и те же требования.
/// </summary>
public class LevelInfo
{
    public enum MissionType
    {
        Incubators,         // Инкубаторы
        MaterialCollection, // Сбор материалов
        ExtractorDefense,   // Защита добытчика
        Clear,              // Зачистка / элитный враг
        Evacuation          // Эвакуация
    }

    /// <summary>Роль региона (мини-локации) в графе уровня.</summary>
    public enum RegionRole
    {
        None,       // обычный регион без задания
        Start,      // точка появления игрока
        Nest,       // гнездо: арена с нишей под инкубатор
        Hub,        // хаб: МППУ и раздатчик взрывчатки
        Deposit,    // месторождение: породы с кристаллами
        Platform,   // площадка над месторождением под ДУ: ровный пол, укрытия
        Arena,      // большая арена для зачистки
        Evacuation, // точка эвакуации

        // Роли дополнительных миссий (квесты Доктора и Инженера)
        Cache,      // охраняемый предмет или объект, который нужно вынести
        Lair        // логово особой цели
    }

    /// <summary>
    /// Вид дополнительной миссии. Дополнительные миссии не берутся из пула основных:
    /// это фиксированные квесты Доктора и Инженера.
    /// </summary>
    public enum SideMissionKind
    {
        RetrieveItem, // найти предмет, который охраняют враги
        CarryOut,     // вынести объект с уровня
        KillTarget    // убить особую цель
    }

    /// <summary>Форма графа. Задаёт её только основная миссия.</summary>
    public enum GraphShape
    {
        Any,
        Fork,   // развилка: ветви с тупиками (Инкубаторы)
        Star,   // звезда вокруг хаба (Сбор материалов)
        Chain,  // цепочка регионов (Защита добытчика)
        Arena,  // одна большая арена (Зачистка)
        Path    // длинный путь вперёд (Эвакуация)
    }

    /// <summary>Сколько альтернативных путей нужно на уровне.</summary>
    public enum LoopAmount
    {
        Any,
        Few,    // почти дерево
        Some,
        Many
    }

    public enum RegionSize
    {
        Any,
        Small,
        Medium,
        Large
    }

    /// <summary>Как считать ожидаемую длительность уровня.</summary>
    public enum DurationModel
    {
        Exploration, // обойти все цели и вернуться: сумма путей до целей
        Shuttle,     // челнок: (нужно кристаллов / лимит переноски) × круг «хаб → месторождение → хаб»
        Defense,     // переходы между площадками + время обороны на каждой
        Arena,       // фиксированное время боя
        Run          // длина пути от старта до точки эвакуации
    }

    /// <summary>
    /// Требование к одной группе регионов с одной ролью.
    /// Расстояния нормированы: 0 = старт, 1 = самый дальний регион по графу (не по прямой).
    /// </summary>
    [Serializable]
    public class RegionRequirement
    {
        public RegionRole Role = RegionRole.None;

        [Tooltip("Пусто = регион нужен основной миссии, иначе id дополнительной миссии.")]
        public string SideMissionId = "";

        [Tooltip("Выкл: требования к связям (входы, тупик, петля, число путей) только желательны, " +
                 "генератор не перестраивает ради них граф. У дополнительных миссий выключено.")]
        public bool ConnectionsRequired = true;

        [Min(0)] public int MinCount = 1;
        [Min(0)] public int MaxCount = 1;

        [Header("Расстояние от старта по графу (0..1)")]
        [Range(0f, 1f)] public float MinDistance = 0f;
        [Range(0f, 1f)] public float MaxDistance = 1f;

        [Tooltip("Минимальное расстояние между регионами этой группы (0..1). Например, гнёзда далеко друг от друга.")]
        [Range(0f, 1f)] public float MinSeparation = 0f;

        [Header("Входы")]
        [Min(0)] public int MinEntrances = 1;
        [Tooltip("0 = без ограничения.")]
        [Min(0)] public int MaxEntrances = 0;

        [Tooltip("Регион должен быть тупиком (ровно один вход).")]
        public bool DeadEnd;

        [Header("Петли")]
        [Tooltip("Регион лежит на петле.")]
        public bool OnLoop;

        [Tooltip("Сколько непересекающихся по рёбрам путей должно вести к региону от старта. 2 = туда и обратно разными дорогами.")]
        [Min(1)] public int MinDisjointPaths = 1;

        public RegionSize Size = RegionSize.Any;

        public bool IsSideMission => !string.IsNullOrEmpty(SideMissionId);

        public RegionRequirement Clone() => (RegionRequirement)MemberwiseClone();
    }

    /// <summary>Требования одной миссии. Форма графа, петли и длительность учитываются только у основной.</summary>
    [Serializable]
    public class MissionRequirements
    {
        public MissionType Mission;
        public GraphShape Shape = GraphShape.Any;
        public LoopAmount Loops = LoopAmount.Any;
        public DurationModel Duration = DurationModel.Exploration;
        public List<RegionRequirement> Regions = new();

        /// <summary>
        /// Черновые требования по таблице GDD. Числа уточняются на плейтестах.
        /// </summary>
        public static MissionRequirements For(MissionType mission)
        {
            var m = new MissionRequirements { Mission = mission };

            switch (mission)
            {
                case MissionType.Incubators:
                    m.Shape = GraphShape.Fork;
                    m.Loops = LoopAmount.Any;
                    m.Duration = DurationModel.Exploration;
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Nest,
                        MinCount = 2, MaxCount = 3,
                        MinDistance = 0.6f, MinSeparation = 0.4f,
                        DeadEnd = true, Size = RegionSize.Medium
                    });
                    break;

                case MissionType.MaterialCollection:
                    m.Shape = GraphShape.Star;
                    m.Loops = LoopAmount.Many;
                    m.Duration = DurationModel.Shuttle;
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Hub,
                        MinCount = 1, MaxCount = 1,
                        MaxDistance = 0.2f, MinEntrances = 3,
                        OnLoop = true, Size = RegionSize.Medium
                    });
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Deposit,
                        MinCount = 3, MaxCount = 5,
                        MinDistance = 0.5f, MinSeparation = 0.2f,
                        MinDisjointPaths = 2
                    });
                    break;

                case MissionType.ExtractorDefense:
                    m.Shape = GraphShape.Chain;
                    m.Loops = LoopAmount.Some;
                    m.Duration = DurationModel.Defense;
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Platform,
                        MinCount = 2, MaxCount = 3,
                        MinDistance = 0.3f, MinSeparation = 0.25f,
                        MinEntrances = 3, Size = RegionSize.Medium
                    });
                    break;

                case MissionType.Clear:
                    m.Shape = GraphShape.Arena;
                    m.Loops = LoopAmount.Any;
                    m.Duration = DurationModel.Arena;
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Arena,
                        MinCount = 1, MaxCount = 1,
                        MinDistance = 0.4f, MinEntrances = 3,
                        Size = RegionSize.Large
                    });
                    break;

                case MissionType.Evacuation:
                    m.Shape = GraphShape.Path;
                    m.Loops = LoopAmount.Few;
                    m.Duration = DurationModel.Run;
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Evacuation,
                        MinCount = 1, MaxCount = 1,
                        MinDistance = 1f
                    });
                    break;
            }

            return m;
        }
    }

    /// <summary>
    /// Требования дополнительной миссии. Она не меняет форму уровня, а только добавляет регионы.
    /// Требования к самому региону (роль, размер, расстояние) обязательны,
    /// требования к связям только желательны.
    /// </summary>
    [Serializable]
    public class SideMissionRequirements
    {
        [Tooltip("Уникальный id квеста, например \"doctor_memory_03\".")]
        public string Id = "";
        public SideMissionKind Kind;
        public List<RegionRequirement> Regions = new();

        /// <summary>Черновые требования по виду миссии. Конкретный квест может их переопределить.</summary>
        public static SideMissionRequirements For(string id, SideMissionKind kind)
        {
            var m = new SideMissionRequirements { Id = id, Kind = kind };

            switch (kind)
            {
                case SideMissionKind.RetrieveItem:
                    // Предмет в стороне от основного пути, у входа стоит охрана
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Cache,
                        MinDistance = 0.4f,
                        DeadEnd = true, Size = RegionSize.Medium
                    });
                    break;

                case SideMissionKind.CarryOut:
                    // Объект далеко, назад с грузом лучше идти другой дорогой
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Cache,
                        MinDistance = 0.5f,
                        MinDisjointPaths = 2
                    });
                    break;

                case SideMissionKind.KillTarget:
                    // Логово, где цель можно обойти и встретить с разных сторон
                    m.Regions.Add(new RegionRequirement
                    {
                        Role = RegionRole.Lair,
                        MinDistance = 0.5f,
                        MinEntrances = 2, Size = RegionSize.Large
                    });
                    break;
            }

            return m;
        }
    }

    /// <summary>
    /// Итоговые требования к уровню: результат слияния основной и дополнительных миссий.
    /// Это то, что получает генератор ветки.
    /// </summary>
    [Serializable]
    public class LevelRequirements
    {
        public MissionType MainMission;
        public GraphShape Shape;
        public LoopAmount Loops;
        public DurationModel Duration;

        [Tooltip("Целевая длительность уровня в секундах; по ней уровни разных миссий выравниваются по длине.")]
        public Vector2 TargetDurationSeconds = new(300f, 480f);

        [Tooltip("Скрытые точки разломов Искажения в каждом регионе. С запасом: разлом не открывается в поле зрения и рядом с игроком.")]
        [Min(1)] public int MinRiftPointsPerRegion = 3;

        public List<RegionRequirement> Regions = new();

        /// <summary>
        /// Скелет графа (форма, петли, длительность) задаёт основная миссия.
        /// Дополнительные миссии только добавляют регионы со своими ролями.
        /// </summary>
        public static LevelRequirements Merge(MissionRequirements main, IEnumerable<SideMissionRequirements> side)
        {
            var result = new LevelRequirements
            {
                MainMission = main.Mission,
                Shape = main.Shape,
                Loops = main.Loops,
                Duration = main.Duration
            };

            result.Regions.Add(new RegionRequirement
            {
                Role = RegionRole.Start,
                MinCount = 1, MaxCount = 1, MaxDistance = 0f
            });

            foreach (var r in main.Regions)
                result.Regions.Add(r.Clone());

            if (side != null)
                foreach (var mission in side)
                foreach (var r in mission.Regions)
                {
                    var copy = r.Clone();
                    copy.SideMissionId = mission.Id;
                    copy.ConnectionsRequired = false;
                    result.Regions.Add(copy);
                }

            return result;
        }

        public int MinRegionCount()
        {
            int sum = 0;
            foreach (var r in Regions)
                sum += r.MinCount;
            return sum;
        }
    }
}
