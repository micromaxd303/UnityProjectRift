using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Система запекания навигационной сетки через Job System.
    /// 
    /// ПОЧЕМУ JOB SYSTEM:
    /// При 500³ узлов = 125 миллионов проверок коллизий.
    /// Обычный цикл: ~10 минут
    /// Job System + OverlapSphereCommand: ~10-30 секунд
    /// 
    /// КАК ЭТО РАБОТАЕТ:
    /// 1. Создаём массив OverlapSphereCommand — каждая команда это одна проверка
    /// 2. Unity выполняет их параллельно на всех ядрах CPU
    /// 3. Получаем результаты и заполняем чанки
    /// 
    /// ОГРАНИЧЕНИЯ:
    /// - OverlapSphereCommand.ScheduleBatch имеет лимит на количество команд
    /// - Поэтому обрабатываем чанками (batches)
    /// </summary>
    public static class NavMesh3DBaker
    {
        /// <summary>
        /// Максимальное количество команд в одном батче.
        /// Слишком много — может не хватить памяти.
        /// Слишком мало — много накладных расходов.
        /// 65536 = 16 чанков (16³ × 16) — хороший баланс.
        /// </summary>
        private const int MaxCommandsPerBatch = 65536;
        
        /// <summary>
        /// Результат запекания.
        /// </summary>
        public struct BakeResult
        {
            public bool success;
            public int totalNodes;
            public int walkableNodes;
            public int blockedNodes;
            public float timeSeconds;
            public string errorMessage;
        }
        
        /// <summary>
        /// Запекает навигационную сетку.
        /// </summary>
        /// <param name="chunks">3D массив чанков для заполнения</param>
        /// <param name="bounds">Границы навмеша</param>
        /// <param name="chunkCount">Количество чанков по осям</param>
        /// <param name="cellSize">Размер ячейки</param>
        /// <param name="agentRadius">Радиус агента</param>
        /// <param name="obstacleLayers">Слои препятствий</param>
        /// <param name="showProgress">Показывать прогресс-бар (только в Editor)</param>
        public static BakeResult Bake(
            NavMesh3DChunk[,,] chunks,
            NavMesh3DBounds bounds,
            int3 chunkCount,
            float cellSize,
            float agentRadius,
            int obstacleLayers,
            bool showProgress = true)
        {
            var startTime = DateTime.Now;
            var result = new BakeResult();
            
            try
            {
                int totalChunks = chunkCount.x * chunkCount.y * chunkCount.z;
                int nodesPerChunk = NavMesh3DChunk.NodeCount;
                result.totalNodes = totalChunks * nodesPerChunk;
                
                // Вычисляем сколько чанков обрабатывать за раз
                int chunksPerBatch = Mathf.Max(1, MaxCommandsPerBatch / nodesPerChunk);
                int totalBatches = (totalChunks + chunksPerBatch - 1) / chunksPerBatch;
                
                int processedChunks = 0;
                
                for (int batch = 0; batch < totalBatches; batch++)
                {
                    // Определяем диапазон чанков для этого батча
                    int startChunk = batch * chunksPerBatch;
                    int endChunk = Mathf.Min(startChunk + chunksPerBatch, totalChunks);
                    int chunksInBatch = endChunk - startChunk;
                    int nodesInBatch = chunksInBatch * nodesPerChunk;
                    
                    // Показываем прогресс
                    #if UNITY_EDITOR
                    if (showProgress)
                    {
                        float progress = (float)processedChunks / totalChunks;
                        if (UnityEditor.EditorUtility.DisplayCancelableProgressBar(
                            "Baking NavMesh3D",
                            $"Processing chunks {startChunk}-{endChunk} of {totalChunks} ({progress * 100:F0}%)",
                            progress))
                        {
                            // Пользователь нажал Cancel
                            UnityEditor.EditorUtility.ClearProgressBar();
                            result.success = false;
                            result.errorMessage = "Cancelled by user";
                            return result;
                        }
                    }
                    #endif
                    
                    // Запекаем батч
                    BakeBatch(
                        chunks, 
                        bounds, 
                        chunkCount, 
                        cellSize, 
                        agentRadius, 
                        obstacleLayers,
                        startChunk, 
                        endChunk,
                        ref result);
                    
                    processedChunks += chunksInBatch;
                }
                
                #if UNITY_EDITOR
                if (showProgress)
                {
                    UnityEditor.EditorUtility.ClearProgressBar();
                }
                #endif
                
                result.success = true;
                result.blockedNodes = result.totalNodes - result.walkableNodes;
                result.timeSeconds = (float)(DateTime.Now - startTime).TotalSeconds;
            }
            catch (Exception e)
            {
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.ClearProgressBar();
                #endif
                
                result.success = false;
                result.errorMessage = e.Message;
                Debug.LogError($"NavMesh3DBaker: {e}");
            }
            
            return result;
        }
        
        /// <summary>
        /// Запекает один батч чанков.
        /// </summary>
        private static void BakeBatch(
            NavMesh3DChunk[,,] chunks,
            NavMesh3DBounds bounds,
            int3 chunkCount,
            float cellSize,
            float agentRadius,
            int obstacleLayers,
            int startChunkIndex,
            int endChunkIndex,
            ref BakeResult result)
        {
            int chunksInBatch = endChunkIndex - startChunkIndex;
            int nodesPerChunk = NavMesh3DChunk.NodeCount;
            int totalNodes = chunksInBatch * nodesPerChunk;
            
            // Аллоцируем NativeArray для Job System
            var nodePositions = new NativeArray<float3>(totalNodes, Allocator.TempJob);
            var overlapCommands = new NativeArray<OverlapSphereCommand>(totalNodes, Allocator.TempJob);
            var overlapResults = new NativeArray<ColliderHit>(totalNodes, Allocator.TempJob);
            var chunkIndices = new NativeArray<int3>(chunksInBatch, Allocator.TempJob);
            
            try
            {
                // Заполняем индексы чанков для этого батча
                for (int i = 0; i < chunksInBatch; i++)
                {
                    int globalIndex = startChunkIndex + i;
                    chunkIndices[i] = new int3(
                        (globalIndex % (chunkCount.x * chunkCount.z)) / chunkCount.z,
                        globalIndex / (chunkCount.x * chunkCount.z),
                        globalIndex % chunkCount.z
                    );
                }
                
                // Реальный размер сетки (может быть больше bounds из-за округления)
                float3 gridSize = (float3)chunkCount * NavMesh3DSettings.ChunkSize * cellSize;
                
                // Job 1: Вычисляем позиции всех узлов
                var calculatePositionsJob = new CalculateNodePositionsJob
                {
                    chunkIndices = chunkIndices,
                    nodePositions = nodePositions,
                    boundsCenter = bounds.center,
                    gridSize = gridSize,  // Используем gridSize вместо boundsSize
                    boundsRotation = bounds.rotation,
                    cellSize = cellSize,
                    chunkSize = NavMesh3DSettings.ChunkSize,
                    nodesPerChunk = nodesPerChunk
                };
                
                var positionsHandle = calculatePositionsJob.Schedule(totalNodes, 64);
                positionsHandle.Complete();
                
                // Job 2: Создаём команды для проверки коллизий
                var createCommandsJob = new CreateOverlapCommandsJob
                {
                    nodePositions = nodePositions,
                    commands = overlapCommands,
                    radius = agentRadius,
                    layerMask = obstacleLayers
                };
                
                var commandsHandle = createCommandsJob.Schedule(totalNodes, 64);
                commandsHandle.Complete();
                
                // Job 3: Выполняем проверку коллизий (это магия Unity!)
                // ScheduleBatch выполняет все проверки параллельно
                var overlapHandle = OverlapSphereCommand.ScheduleBatch(
                    overlapCommands, 
                    overlapResults, 
                    1,      // minCommandsPerJob — 1 для максимальной параллельности
                    1,      // maxHits — нам нужен только факт коллизии
                    default
                );
                overlapHandle.Complete();
                
                // Записываем результаты в чанки
                for (int i = 0; i < chunksInBatch; i++)
                {
                    int3 chunkCoord = chunkIndices[i];
                    var chunk = chunks[chunkCoord.x, chunkCoord.y, chunkCoord.z];
                    
                    int nodeOffset = i * nodesPerChunk;
                    
                    for (int n = 0; n < nodesPerChunk; n++)
                    {
                        // Если есть коллайдер — узел заблокирован
                        bool blocked = overlapResults[nodeOffset + n].instanceID != 0;
                        
                        chunk.SetWalkable(n, !blocked);
                        
                        if (!blocked)
                            result.walkableNodes++;
                    }
                }
            }
            finally
            {
                // Освобождаем память
                nodePositions.Dispose();
                overlapCommands.Dispose();
                overlapResults.Dispose();
                chunkIndices.Dispose();
            }
        }
        
        /// <summary>
        /// Job для вычисления мировых позиций узлов.
        /// IJobParallelFor — выполняется параллельно на всех ядрах.
        /// </summary>
        private struct CalculateNodePositionsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int3> chunkIndices;
            [WriteOnly] public NativeArray<float3> nodePositions;
            
            public float3 boundsCenter;
            public float3 gridSize;  // Реальный размер сетки чанков
            public quaternion boundsRotation;
            public float cellSize;
            public int chunkSize;
            public int nodesPerChunk;
            
            public void Execute(int index)
            {
                // Определяем какой это чанк и какой узел внутри
                int chunkLocalIndex = index / nodesPerChunk;
                int nodeLocalIndex = index % nodesPerChunk;
                
                int3 chunkCoord = chunkIndices[chunkLocalIndex];
                
                // Локальные координаты узла внутри чанка
                int y = nodeLocalIndex / (chunkSize * chunkSize);
                int x = (nodeLocalIndex % (chunkSize * chunkSize)) / chunkSize;
                int z = nodeLocalIndex % chunkSize;
                
                // Абсолютные координаты узла в сетке
                int3 absoluteCoord = new int3(
                    chunkCoord.x * chunkSize + x,
                    chunkCoord.y * chunkSize + y,
                    chunkCoord.z * chunkSize + z
                );
                
                // Позиция в локальных координатах (от угла сетки)
                float3 localPos = (float3)absoluteCoord * cellSize + cellSize * 0.5f;
                
                // Смещаем чтобы центр сетки совпадал с центром bounds
                localPos -= gridSize * 0.5f;
                
                // Переводим в мировые координаты
                nodePositions[index] = boundsCenter + math.mul(boundsRotation, localPos);
            }
        }
        
        /// <summary>
        /// Job для создания команд проверки коллизий.
        /// </summary>
        private struct CreateOverlapCommandsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> nodePositions;
            [WriteOnly] public NativeArray<OverlapSphereCommand> commands;
            
            public float radius;
            public int layerMask;
            
            public void Execute(int index)
            {
                commands[index] = new OverlapSphereCommand(
                    nodePositions[index],
                    radius,
                    new QueryParameters
                    {
                        layerMask = layerMask,
                        hitTriggers = QueryTriggerInteraction.Ignore
                    }
                );
            }
        }
    }
}
