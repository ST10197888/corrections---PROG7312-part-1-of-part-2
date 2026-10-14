using System;
using System.Collections;
using System.Collections.Generic;

namespace SmartX.Core
{
    public sealed class GenericRingBuffer<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;
        private int _start;
        private int _count;

        public GenericRingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "Capacity must be greater than zero.");

            _items = new T[capacity];
        }

        public int Capacity => _items.Length;

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return _items[(_start + index) % Capacity];
            }
        }

        public void Add(T item)
        {
            if (_count < Capacity)
            {
                _items[(_start + _count) % Capacity] = item;
                _count++;
                return;
            }

            _items[_start] = item;
            _start = (_start + 1) % Capacity;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _start = 0;
            _count = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
