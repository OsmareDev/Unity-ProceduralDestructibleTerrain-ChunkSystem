using System;
using System.Collections.Generic;
using UnityEngine;

public interface IPoolable {
    void OnActivate();
    void OnDeactivate();
    void Dispose();
}

public class GenericPool<T> where T : IPoolable {
    private Stack<T> m_pool;
    private Func<T> m_factory;
    private int m_maxPoolSize;

    public GenericPool( Func<T> factory, int initialSize = 20, int maxSize = 100 ) {
        m_factory = factory;
        m_maxPoolSize = maxSize;
        m_pool = new Stack<T>(initialSize);

        for (int i = 0; i < initialSize; i++) {
            T item = m_factory();
            item.OnDeactivate();
            m_pool.Push(item);
        }
    }

    public T Get() {
        T item = m_pool.Count > 0 ? m_pool.Pop() : m_factory();
        item.OnActivate();
        return item;
    }

    public void Return(T item) {
        if (m_pool.Count < m_maxPoolSize) {
            item.OnDeactivate();
            m_pool.Push(item);
        } else {
            item.Dispose();
        }
    }

    public void Clear() {
        while (m_pool.Count > 0) {
            T item = m_pool.Pop();
            item.Dispose();
        }
    }

    public int PoolCount => m_pool.Count;
}
