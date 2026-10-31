using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.Collections;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Чанк навигационной сетки — блок 16x16x16 узлов.
    /// 
    /// Почему именно такая структура:
    /// 
    /// 1. РАЗМЕР 16³:
    ///    - 4096 узлов — хорошо помещается в L1 кэш CPU (32-64 KB)
    ///    - Степень двойки — быстрые битовые операции вместо деления
    ///    - При cellSize=0.5 это 8x8x8 метров — разумный размер для игрового пространства
    /// 
    /// 2. БИТОВАЯ МАСКА вместо массива bool:
    ///    - bool[] для 4096 узлов = 4096 байт
    ///    - Битовая маска = 512 байт (в 8 раз меньше!)
    ///    - Это важно при 30,000 чанков — экономим ~100 MB RAM
    /// 
    /// 3. REGION ID:
    ///    - Позволяет мгновенно определить связность
    ///    - Если два узла в разных регионах — пути нет, не ищем
    /// </summary>
    public class NavMesh3DChunk
    {
        // ===== Константы =====
        
        /// <summary>Размер чанка по каждой оси</summary>
        public const int Size = NavMesh3DSettings.ChunkSize; // 16
        
        /// <summary>Количество узлов в чанке</summary>
        public const int NodeCount = Size * Size * Size; // 4096
        
        /// <summary>Количество ulong для хранения битовой маски (4096 / 64 = 64)</summary>
        private const int BitMaskLongs = NodeCount / 64;
        
        // ===== Данные чанка =====
        
        /// <summary>Координаты этого чанка в сетке чанков</summary>
        public readonly ChunkCoord Coord;
        
        /// <summary>
        /// Битовая маска проходимости узлов.
        /// Бит = 1 означает узел проходим.
        /// Индексация: бит (y * 256 + x * 16 + z)
        /// </summary>
        private readonly ulong[] walkableMask;
        
        /// <summary>
        /// ID региона, к которому принадлежит этот чанк.
        /// -1 = регион не назначен (чанк полностью заблокирован или не обработан)
        /// </summary>
        public int RegionId { get; set; } = -1;
        
        /// <summary>Количество проходимых узлов в чанке</summary>
        public int WalkableCount { get; private set; }
        
        /// <summary>Чанк полностью заблокирован (нет проходимых узлов)</summary>
        public bool IsFullyBlocked => WalkableCount == 0;
        
        /// <summary>Чанк полностью проходим</summary>
        public bool IsFullyWalkable => WalkableCount == NodeCount;
        
        // ===== Конструктор =====
        
        public NavMesh3DChunk(ChunkCoord coord)
        {
            Coord = coord;
            walkableMask = new ulong[BitMaskLongs];
            WalkableCount = 0;
        }
        
        // ===== Методы работы с узлами =====
        
        /// <summary>
        /// Проверяет, проходим ли узел.
        /// [MethodImpl(AggressiveInlining)] — подсказка компилятору встроить метод.
        /// Это критично для производительности, т.к. вызывается миллионы раз при поиске пути.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWalkable(int x, int y, int z)
        {
            int index = y * Size * Size + x * Size + z;
            int longIndex = index >> 6;      // index / 64 — какой ulong
            int bitIndex = index & 63;       // index % 64 — какой бит
            return (walkableMask[longIndex] & (1UL << bitIndex)) != 0;
        }
        
        /// <summary>
        /// Проверяет проходимость по линейному индексу.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWalkable(int index)
        {
            int longIndex = index >> 6;
            int bitIndex = index & 63;
            return (walkableMask[longIndex] & (1UL << bitIndex)) != 0;
        }
        
        /// <summary>
        /// Проверяет проходимость по локальным координатам.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsWalkable(LocalNodeCoord coord)
        {
            return IsWalkable(coord.x, coord.y, coord.z);
        }
        
        /// <summary>
        /// Устанавливает проходимость узла.
        /// </summary>
        public void SetWalkable(int x, int y, int z, bool walkable)
        {
            int index = y * Size * Size + x * Size + z;
            int longIndex = index >> 6;
            int bitIndex = index & 63;
            
            bool wasWalkable = (walkableMask[longIndex] & (1UL << bitIndex)) != 0;
            
            if (walkable)
            {
                walkableMask[longIndex] |= (1UL << bitIndex);  // Установить бит
                if (!wasWalkable) WalkableCount++;
            }
            else
            {
                walkableMask[longIndex] &= ~(1UL << bitIndex); // Сбросить бит
                if (wasWalkable) WalkableCount--;
            }
        }
        
        /// <summary>
        /// Устанавливает проходимость по индексу.
        /// </summary>
        public void SetWalkable(int index, bool walkable)
        {
            int longIndex = index >> 6;
            ulong bit = 1UL << (index & 63);
            bool wasWalkable = (walkableMask[longIndex] & bit) != 0;
            
            if (walkable == wasWalkable)
                return;
            
            if (walkable)
            {
                walkableMask[longIndex] |= bit;
                WalkableCount++;
            }
            else
            {
                walkableMask[longIndex] &= ~bit;
                WalkableCount--;
            }
        }
        
        /// <summary>
        /// Заполняет весь чанк одним значением.
        /// Оптимизированная версия — не вызывает SetWalkable для каждого узла.
        /// </summary>
        public void Fill(bool walkable)
        {
            ulong value = walkable ? ulong.MaxValue : 0UL;
            for (int i = 0; i < BitMaskLongs; i++)
            {
                walkableMask[i] = value;
            }
            WalkableCount = walkable ? NodeCount : 0;
        }
        
        /// <summary>
        /// Загружает данные из NativeArray (результат Job System).
        /// Используется при запекании.
        /// </summary>
        public void LoadFromArray(NativeArray<NodeStatus> statuses, int startIndex)
        {
            WalkableCount = 0;
            
            for (int i = 0; i < NodeCount; i++)
            {
                bool walkable = statuses[startIndex + i] == NodeStatus.Walkable;
                
                int longIndex = i >> 6;
                int bitIndex = i & 63;
                
                if (walkable)
                {
                    walkableMask[longIndex] |= (1UL << bitIndex);
                    WalkableCount++;
                }
                else
                {
                    walkableMask[longIndex] &= ~(1UL << bitIndex);
                }
            }
        }
        
        // ===== Методы для поиска пути =====
        
        /// <summary>
        /// Возвращает индексы всех проходимых соседей узла.
        /// Использует stackalloc для избежания аллокаций.
        /// 
        /// Neighbours — это 6 соседей (±X, ±Y, ±Z).
        /// Можно расширить до 26 для диагонального движения.
        /// </summary>
        /// <param name="index">Индекс узла</param>
        /// <param name="neighbors">Буфер для записи соседей (минимум 6 элементов)</param>
        /// <returns>Количество найденных соседей</returns>
        public int GetWalkableNeighbors(int index, Span<int> neighbors)
        {
            var coord = LocalNodeCoord.FromIndex(index);
            int count = 0;
            
            // Смещения для 6 направлений
            Span<int3> offsets = stackalloc int3[6]
            {
                new int3(1, 0, 0),   // +X
                new int3(-1, 0, 0),  // -X
                new int3(0, 1, 0),   // +Y
                new int3(0, -1, 0),  // -Y
                new int3(0, 0, 1),   // +Z
                new int3(0, 0, -1)   // -Z
            };
            
            for (int i = 0; i < 6; i++)
            {
                int nx = coord.x + offsets[i].x;
                int ny = coord.y + offsets[i].y;
                int nz = coord.z + offsets[i].z;
                
                // Проверяем границы чанка
                if (nx >= 0 && nx < Size && 
                    ny >= 0 && ny < Size && 
                    nz >= 0 && nz < Size)
                {
                    if (IsWalkable(nx, ny, nz))
                    {
                        neighbors[count++] = ny * Size * Size + nx * Size + nz;
                    }
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Проверяет, есть ли проходимые узлы на границе в указанном направлении.
        /// Используется для определения связности чанков.
        /// </summary>
        /// <param name="direction">0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z</param>
        public bool HasWalkableBorder(int direction)
        {
            // Определяем какую грань проверять
            int fixedAxis = direction / 2;  // 0=X, 1=Y, 2=Z
            int fixedValue = (direction % 2 == 0) ? (Size - 1) : 0;
            
            // Перебираем все узлы на грани
            for (int a = 0; a < Size; a++)
            {
                for (int b = 0; b < Size; b++)
                {
                    int x, y, z;
                    
                    switch (fixedAxis)
                    {
                        case 0: // X фиксирован
                            x = fixedValue; y = a; z = b;
                            break;
                        case 1: // Y фиксирован
                            x = a; y = fixedValue; z = b;
                            break;
                        default: // Z фиксирован
                            x = a; y = b; z = fixedValue;
                            break;
                    }
                    
                    if (IsWalkable(x, y, z))
                        return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Подсчитывает проходимые узлы на границе.
        /// </summary>
        public int CountWalkableBorder(int direction)
        {
            int fixedAxis = direction / 2;
            int fixedValue = (direction % 2 == 0) ? (Size - 1) : 0;
            int count = 0;
            
            for (int a = 0; a < Size; a++)
            {
                for (int b = 0; b < Size; b++)
                {
                    int x, y, z;
                    
                    switch (fixedAxis)
                    {
                        case 0: x = fixedValue; y = a; z = b; break;
                        case 1: x = a; y = fixedValue; z = b; break;
                        default: x = a; y = b; z = fixedValue; break;
                    }
                    
                    if (IsWalkable(x, y, z))
                        count++;
                }
            }
            
            return count;
        }
        
        // ===== Утилиты =====
        
        /// <summary>
        /// Конвертирует локальные координаты узла в мировые.
        /// </summary>
        public float3 LocalToWorld(int x, int y, int z, NavMesh3DBounds bounds, float cellSize)
        {
            // Абсолютные координаты узла в сетке
            int3 absolute = new int3(
                Coord.x * Size + x,
                Coord.y * Size + y,
                Coord.z * Size + z
            );
            
            // Переводим в мировые координаты
            float3 localPos = (float3)absolute * cellSize;
            float3 offset = bounds.size * 0.5f; // Сдвиг от центра к углу
            
            return bounds.center 
                   + math.mul(bounds.rotation, localPos - offset);
        }
        
        public override string ToString()
        {
            return $"Chunk{Coord} [Walkable: {WalkableCount}/{NodeCount}, Region: {RegionId}]";
        }
    }
}
