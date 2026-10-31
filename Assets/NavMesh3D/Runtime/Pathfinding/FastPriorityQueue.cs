using System;
using System.Runtime.CompilerServices;

namespace NavMesh3DSystem
{
    /// <summary>
    /// Быстрая очередь с приоритетом на основе Binary Min-Heap.
    /// Оптимизирована для A* алгоритма.
    /// 
    /// ПОЧЕМУ СВОЙ HEAP:
    /// 1. Встроенный PriorityQueue может отсутствовать в некоторых версиях Unity
    /// 2. Мы можем оптимизировать под наши нужды (int элементы, float приоритеты)
    /// 3. Избегаем boxing/unboxing
    /// 
    /// СЛОЖНОСТЬ:
    /// - Enqueue: O(log n)
    /// - Dequeue: O(log n)
    /// - Peek: O(1)
    /// - Count: O(1)
    /// </summary>
    public class FastPriorityQueue
    {
        // Храним элементы и приоритеты в параллельных массивах
        // Это быстрее чем массив структур из-за кэш-локальности при сравнениях
        private int[] elements;
        private float[] priorities;
        private int count;
        
        /// <summary>
        /// Количество элементов в очереди.
        /// </summary>
        public int Count => count;
        
        /// <summary>
        /// Создаёт очередь с начальной ёмкостью.
        /// </summary>
        public FastPriorityQueue(int initialCapacity = 256)
        {
            elements = new int[initialCapacity];
            priorities = new float[initialCapacity];
            count = 0;
        }
        
        /// <summary>
        /// Добавляет элемент с указанным приоритетом.
        /// Меньший приоритет = выше в очереди.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(int element, float priority)
        {
            // Увеличиваем массивы если нужно
            if (count >= elements.Length)
            {
                Grow();
            }
            
            // Добавляем в конец
            int index = count;
            elements[index] = element;
            priorities[index] = priority;
            count++;
            
            // Всплываем вверх (bubble up)
            BubbleUp(index);
        }
        
        /// <summary>
        /// Извлекает элемент с наименьшим приоритетом.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Dequeue()
        {
            if (count == 0)
                throw new InvalidOperationException("Queue is empty");
            
            int result = elements[0];
            
            // Перемещаем последний элемент в корень
            count--;
            if (count > 0)
            {
                elements[0] = elements[count];
                priorities[0] = priorities[count];
                
                // Просеиваем вниз (bubble down)
                BubbleDown(0);
            }
            
            return result;
        }
        
        /// <summary>
        /// Возвращает элемент с наименьшим приоритетом без удаления.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek()
        {
            if (count == 0)
                throw new InvalidOperationException("Queue is empty");
            
            return elements[0];
        }
        
        /// <summary>
        /// Очищает очередь.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            count = 0;
            // Не очищаем массивы — просто сбрасываем счётчик
        }
        
        /// <summary>
        /// Всплытие элемента вверх по куче.
        /// Вызывается после добавления.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) >> 1; // (index - 1) / 2
                
                // Если родитель меньше или равен — всё ок
                if (priorities[parentIndex] <= priorities[index])
                    break;
                
                // Меняем местами с родителем
                Swap(index, parentIndex);
                index = parentIndex;
            }
        }
        
        /// <summary>
        /// Просеивание элемента вниз по куче.
        /// Вызывается после удаления корня.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BubbleDown(int index)
        {
            while (true)
            {
                int leftChild = (index << 1) + 1;  // index * 2 + 1
                int rightChild = (index << 1) + 2; // index * 2 + 2
                int smallest = index;
                
                // Находим наименьший из текущего и детей
                if (leftChild < count && priorities[leftChild] < priorities[smallest])
                {
                    smallest = leftChild;
                }
                
                if (rightChild < count && priorities[rightChild] < priorities[smallest])
                {
                    smallest = rightChild;
                }
                
                // Если текущий уже наименьший — готово
                if (smallest == index)
                    break;
                
                // Меняем местами и продолжаем
                Swap(index, smallest);
                index = smallest;
            }
        }
        
        /// <summary>
        /// Обмен двух элементов.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap(int i, int j)
        {
            // Элементы
            int tempElement = elements[i];
            elements[i] = elements[j];
            elements[j] = tempElement;
            
            // Приоритеты
            float tempPriority = priorities[i];
            priorities[i] = priorities[j];
            priorities[j] = tempPriority;
        }
        
        /// <summary>
        /// Увеличивает ёмкость массивов в 2 раза.
        /// </summary>
        private void Grow()
        {
            int newCapacity = elements.Length * 2;
            
            var newElements = new int[newCapacity];
            var newPriorities = new float[newCapacity];
            
            Array.Copy(elements, newElements, count);
            Array.Copy(priorities, newPriorities, count);
            
            elements = newElements;
            priorities = newPriorities;
        }
    }
}
