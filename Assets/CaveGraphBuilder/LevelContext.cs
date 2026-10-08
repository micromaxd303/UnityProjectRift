using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Этап 8 пайплайна: готовый уровень глазами игры.
/// Все позиции и направления — в локальных координатах Root (объекта пещеры), чтобы они двигались
/// вместе с ней. В мир переводятся через World() и WorldDirection().
/// Не зависит от способа генерации: игра читает только это, а не граф, SDF или воксели.
/// </summary>
public class LevelContext
{
    public LevelRequest Request;
    /// <summary>Сид, с которым уровень построен на самом деле (после перестроек графа может отличаться от Request.Seed).</summary>
    public int Seed;
    /// <summary>Уровень прошёл проверку проходимости (этап 4). false — проверка выключена или не прошла.</summary>
    public bool Validated;
    /// <summary>Объект, под которым лежат чанки меша.</summary>
    public Transform Root;

    public Vector3 World(Vector3 local) => Root != null ? Root.TransformPoint(local) : local;
    public Vector3 WorldDirection(Vector3 local) => Root != null ? Root.TransformDirection(local) : local;

    public Vector3 PlayerSpawn;
    public int StartRegion = -1;

    /// <summary>Оценка времени прохождения по графу, с.</summary>
    public float EstimatedDurationSeconds;
    public Vector2 TargetDurationSeconds;
    /// <summary>Сколько скрытых точек разломов нужно в каждом регионе (заполняет этап 7).</summary>
    public int MinRiftPointsPerRegion;

    public readonly List<LevelRegion> Regions = new();
    public readonly List<LevelConnection> Connections = new();
    public readonly List<LevelAnchor> Anchors = new();
    public readonly List<LevelObjective> Objectives = new();
    /// <summary>Точки разломов Искажения (заполняет этап 7). Какой открыть, решает игра: не в поле зрения и не рядом с игроком.</summary>
    public readonly List<LevelRift> Rifts = new();
    /// <summary>Точки на потолке под светящиеся кристаллы (заполняет этап 7).</summary>
    public readonly List<LevelCeilingPoint> CeilingPoints = new();

    public IEnumerable<LevelRegion> RegionsWithRole(LevelInfo.RegionRole role)
    {
        foreach (var r in Regions)
            if (r.Role == role)
                yield return r;
    }

    public IEnumerable<LevelAnchor> AnchorsOfKind(LevelAnchorKind kind)
    {
        foreach (var a in Anchors)
            if (a.Kind == kind)
                yield return a;
    }

    public LevelObjective MainObjective => Objectives.Find(o => o.IsMain);
}

/// <summary>Регион уровня: комната пещеры (или аналог в других генераторах).</summary>
public class LevelRegion
{
    public int Index;
    public LevelInfo.RegionRole Role;
    /// <summary>Не пусто — регион добавлен дополнительной миссией с этим Id.</summary>
    public string SideMissionId = "";
    public Vector3 Center;
    public float Radius;
    /// <summary>Удалённость от старта по графу, 0 — старт, 1 — самый дальний регион.</summary>
    public float Distance01;
    /// <summary>Индексы соединений из LevelContext.Connections.</summary>
    public readonly List<int> Connections = new();

    /// <summary>Точки начальных врагов на полу (заполняет этап 7).</summary>
    public readonly List<Vector3> SpawnPoints = new();
    /// <summary>Индексы разломов этого региона в LevelContext.Rifts.</summary>
    public readonly List<int> Rifts = new();
}

public enum LevelConnectionType
{
    Walk,  // обычный проход
    Drop,  // обрыв: только сверху вниз
    Crawl  // лаз: только в приседе
}

public class LevelConnection
{
    public int A, B;
    public LevelConnectionType Type;
    /// <summary>Входит в петлю (есть обходной путь).</summary>
    public bool IsLoop;
    /// <summary>У обрыва — верхний регион: пройти можно только из него.</summary>
    public int From = -1;

    public bool IsOneWay => Type == LevelConnectionType.Drop;
    public int Other(int region) => region == A ? B : A;
}

public enum LevelAnchorKind
{
    Incubator, // ниша под инкубатор
    Pad,       // ровный пятачок: ДУ на площадке, МППУ и раздатчик в хабе
    Cover,
    Pillar,
    CrystalVein, // месторождение: место в стене под взрывные кристаллы (Forward — нормаль стены)
    Item,        // тайник: предмет доп. миссии (найти или вынести)
    Lair,        // логово: где стоит особая цель
    Evacuation,  // точка эвакуации
    Arena        // центр арены зачистки
}

/// <summary>Готовое место под объект миссии или элемент окружения.</summary>
public class LevelAnchor
{
    public LevelAnchorKind Kind;
    public Vector3 Position;
    public Vector3 Forward;
    public int Region;
    /// <summary>Свободное место вокруг точки на высоте корпуса, м (для логова, эвакуации, арены). 0 — не считалось.</summary>
    public float Radius;
}

/// <summary>Цель миссии: в каких регионах она и какие места под её объекты.</summary>
public class LevelObjective
{
    public bool IsMain;
    public LevelInfo.MissionType Mission;
    public string SideMissionId = "";
    public LevelInfo.SideMissionKind SideKind;
    public readonly List<int> Regions = new();
    /// <summary>Индексы из LevelContext.Anchors (ниши, пятачки в регионах цели).</summary>
    public readonly List<int> Anchors = new();
}

/// <summary>
/// Разлом: разрыв, висящий в воздухе. В пещерах он бьёт лучами в пол, и враги вылезают в местах попадания.
/// </summary>
public class LevelRift
{
    public int Region;
    /// <summary>Центр разрыва в воздухе.</summary>
    public Vector3 Position;
    /// <summary>Пол прямо под разрывом.</summary>
    public Vector3 FloorBelow;
    /// <summary>Куда бьют лучи: точки на полу в прямой видимости от разрыва, с местом над головой под крупного врага.</summary>
    public readonly List<Vector3> GroundPoints = new();
}

/// <summary>
/// Место на потолке под кристалл со светом. Префаб ставится основанием в Position и поворачивается
/// так, чтобы его ось «вниз» совпала с Normal. Подходит любой префаб с радиусом основания не больше MaxRadius.
/// </summary>
public class LevelCeilingPoint
{
    /// <summary>Точка на поверхности потолка.</summary>
    public Vector3 Position;
    /// <summary>Нормаль потолка, смотрит в пещеру (обычно примерно вниз).</summary>
    public Vector3 Normal;
    /// <summary>Радиус участка, где потолок почти плоский и свободен: больший префаб будет висеть краем.</summary>
    public float MaxRadius;
    /// <summary>Насколько кристалл может свисать вниз, не мешая игроку пройти под ним, м.</summary>
    public float MaxLength;
    /// <summary>Комната (индекс региона) или -1, если точка в туннеле.</summary>
    public int Region = -1;
    /// <summary>Туннель (индекс соединения) или -1, если точка в комнате.</summary>
    public int Connection = -1;
}
