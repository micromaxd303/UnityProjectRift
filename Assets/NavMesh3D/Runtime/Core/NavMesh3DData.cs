using System;
using Unity.Mathematics;
using Unity.Collections;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Настройки навигационной сетки.
    /// Сериализуемая структура для отображения в инспекторе Unity.
    /// </summary>
    [Serializable]
    public struct NavMesh3DSettings
    {
        /// <summary>
        /// Размер одной ячейки в мировых единицах.
        /// Меньше = точнее путь, но больше памяти и дольше запекание.
        /// Рекомендуется: 0.25 - 1.0
        /// </summary>
        public float cellSize;
        
        /// <summary>
        /// Радиус агента. Узел считается непроходимым, если в радиусе есть препятствие.
        /// Обычно равен радиусу коллайдера врага.
        /// </summary>
        public float agentRadius;
        
        /// <summary>
        /// Слои, которые считаются препятствиями.
        /// Используется при запекании для Physics.CheckSphere.
        /// </summary>
        public int obstacleLayerMask;
        
        /// <summary>
        /// Размер чанка в узлах по каждой оси.
        /// Всегда 16 — это оптимальный баланс между:
        /// - Размером кэша CPU (16³ = 4096 узлов хорошо кэшируется)
        /// - Количеством чанков (не слишком много для HPA*)
        /// - Гранулярностью регионов
        /// Сделано константой чтобы избежать случайных изменений.
        /// </summary>
        public const int ChunkSize = 16;
        
        /// <summary>
        /// Создаёт настройки со значениями по умолчанию.
        /// </summary>
        public static NavMesh3DSettings Default => new NavMesh3DSettings
        {
            cellSize = 0.5f,
            agentRadius = 0.5f,
            // Всё кроме слоя 6 (в твоём коде это был слой NavMesh)
            obstacleLayerMask = ~(1 << 6)
        };
    }
    
    /// <summary>
    /// Границы навигационного пространства в мировых координатах.
    /// Хранит позицию, размер и поворот volume.
    /// </summary>
    [Serializable]
    public struct NavMesh3DBounds
    {
        public float3 center;      // Центр volume в мировых координатах
        public float3 size;        // Размер в мировых единицах
        public quaternion rotation; // Поворот (для не-axis-aligned volumes)
        
        /// <summary>
        /// Проверяет, находится ли точка внутри границ.
        /// Учитывает поворот volume.
        /// </summary>
        public bool Contains(float3 worldPoint)
        {
            // Переводим точку в локальные координаты volume
            float3 localPoint = math.mul(math.inverse(rotation), worldPoint - center);
            float3 halfSize = size * 0.5f;
            
            // Проверяем каждую ось
            return math.all(math.abs(localPoint) <= halfSize);
        }
        
        /// <summary>
        /// Возвращает ближайшую точку внутри границ.
        /// </summary>
        public float3 ClosestPoint(float3 worldPoint)
        {
            float3 localPoint = math.mul(math.inverse(rotation), worldPoint - center);
            float3 halfSize = size * 0.5f;
            
            // Ограничиваем каждую координату
            float3 clampedLocal = math.clamp(localPoint, -halfSize, halfSize);
            
            // Возвращаем в мировые координаты
            return center + math.mul(rotation, clampedLocal);
        }
    }
    
    /// <summary>
    /// 3D координаты чанка в сетке чанков.
    /// Отдельная структура для типобезопасности — не спутаешь с координатами узла.
    /// </summary>
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int x, y, z;
        
        public ChunkCoord(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
        
        public ChunkCoord(int3 coord)
        {
            x = coord.x;
            y = coord.y;
            z = coord.z;
        }
        
        public int3 ToInt3() => new int3(x, y, z);
        
        /// <summary>
        /// Получить соседний чанк в указанном направлении.
        /// direction: 0-5 соответствует ±X, ±Y, ±Z
        /// </summary>
        public ChunkCoord GetNeighbor(int direction)
        {
            // Таблица смещений для 6 направлений
            // Это быстрее чем switch и легче расширяется
            return direction switch
            {
                0 => new ChunkCoord(x + 1, y, z),  // +X
                1 => new ChunkCoord(x - 1, y, z),  // -X
                2 => new ChunkCoord(x, y + 1, z),  // +Y
                3 => new ChunkCoord(x, y - 1, z),  // -Y
                4 => new ChunkCoord(x, y, z + 1),  // +Z
                5 => new ChunkCoord(x, y, z - 1),  // -Z
                _ => this
            };
        }
        
        // Нужно для использования в Dictionary и HashSet
        public bool Equals(ChunkCoord other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"Chunk({x}, {y}, {z})";
        
        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    }
    
    /// <summary>
    /// Координаты узла внутри чанка (0-15 по каждой оси).
    /// </summary>
    public readonly struct LocalNodeCoord
    {
        public readonly int x, y, z;
        
        public LocalNodeCoord(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
        
        /// <summary>
        /// Преобразует в линейный индекс для массива узлов чанка.
        /// Порядок: Y-major (сначала заполняем слой Y=0, потом Y=1 и т.д.)
        /// Это оптимально для горизонтального движения (частый случай).
        /// </summary>
        public int ToIndex()
        {
            return y * NavMesh3DSettings.ChunkSize * NavMesh3DSettings.ChunkSize 
                 + x * NavMesh3DSettings.ChunkSize 
                 + z;
        }
        
        /// <summary>
        /// Создаёт из линейного индекса.
        /// </summary>
        public static LocalNodeCoord FromIndex(int index)
        {
            int size = NavMesh3DSettings.ChunkSize;
            int y = index / (size * size);
            int x = (index % (size * size)) / size;
            int z = index % size;
            return new LocalNodeCoord(x, y, z);
        }
        
        /// <summary>
        /// Проверяет, валидны ли координаты (0-15).
        /// </summary>
        public bool IsValid()
        {
            int size = NavMesh3DSettings.ChunkSize;
            return x >= 0 && x < size 
                && y >= 0 && y < size 
                && z >= 0 && z < size;
        }
    }
    
    /// <summary>
    /// Глобальные координаты узла (чанк + локальные координаты).
    /// Используется для адресации любого узла в навмеше.
    /// </summary>
    public readonly struct GlobalNodeCoord
    {
        public readonly ChunkCoord chunk;
        public readonly LocalNodeCoord local;
        
        public GlobalNodeCoord(ChunkCoord chunk, LocalNodeCoord local)
        {
            this.chunk = chunk;
            this.local = local;
        }
        
        /// <summary>
        /// Создаёт из абсолютных координат узла в сетке.
        /// Например, узел (20, 5, 33) будет в чанке (1, 0, 2) с локальными (4, 5, 1).
        /// </summary>
        public static GlobalNodeCoord FromAbsolute(int3 absoluteCoord)
        {
            int size = NavMesh3DSettings.ChunkSize;
            
            // Целочисленное деление для координат чанка
            var chunkCoord = new ChunkCoord(
                absoluteCoord.x / size,
                absoluteCoord.y / size,
                absoluteCoord.z / size
            );
            
            // Остаток для локальных координат
            var localCoord = new LocalNodeCoord(
                absoluteCoord.x % size,
                absoluteCoord.y % size,
                absoluteCoord.z % size
            );
            
            return new GlobalNodeCoord(chunkCoord, localCoord);
        }
        
        /// <summary>
        /// Преобразует обратно в абсолютные координаты.
        /// </summary>
        public int3 ToAbsolute()
        {
            int size = NavMesh3DSettings.ChunkSize;
            return new int3(
                chunk.x * size + local.x,
                chunk.y * size + local.y,
                chunk.z * size + local.z
            );
        }
    }
    
    /// <summary>
    /// Информация о соединении между чанками.
    /// Хранит какие узлы на границе чанков соединены.
    /// 
    /// ПРИМЕЧАНИЕ: Эта структура будет использоваться позже для HPA*.
    /// Пока храним только базовую информацию.
    /// </summary>
    public struct ChunkConnection
    {
        public ChunkCoord neighborChunk;  // Координаты соседнего чанка
        public int direction;              // Направление (0-5)
        
        /// <summary>
        /// Количество проходимых узлов на границе.
        /// Используется для быстрой проверки — если 0, проход закрыт.
        /// </summary>
        public int passableCount;
        
        /// <summary>
        /// Битовая маска узлов на границе (256 бит = 4 ulong).
        /// Используем массив вместо fixed buffer для совместимости.
        /// </summary>
        public ulong borderMask0;
        public ulong borderMask1;
        public ulong borderMask2;
        public ulong borderMask3;
        
        public bool IsPassable => passableCount > 0;
    }
    
    /// <summary>
    /// Статус узла. Используем byte для компактности.
    /// В будущем можно добавить типы поверхности, стоимость и т.д.
    /// </summary>
    public enum NodeStatus : byte
    {
        Blocked = 0,    // Непроходим (есть препятствие)
        Walkable = 1,   // Проходим
        // Можно добавить:
        // Water = 2,
        // Slow = 3,
        // и т.д.
    }
}
